"""Compare the original APK, diagnostics only and SMB read-ahead on explicitly supplied media.

--cases points to an ignored JSON array of {id, uri, startMs?, loop?}. Only anonymous
case IDs and playback counters enter reports. No credentials or real paths are logged.
Run each mode with the same cases; baseline uses the separately preserved original APK.
The device's original settings are restored even if a run fails.
"""
import argparse
import hashlib
import json
from pathlib import Path
import re
import subprocess
import threading
import time

ROOT = Path(__file__).resolve().parent.parent
PACKAGE = 'com.linxyi.LsVRPlayer'
EXTERNAL = '/sdcard/Android/data/' + PACKAGE + '/files/'
SNAPSHOT_FIELDS = ('generation', 'monotonicMs', 'state', 'playing', 'position', 'buffered',
                   'bufferAheadMs', 'loading', 'allocatedBufferBytes', 'rebufferCount',
                   'rebufferMs', 'bufferReason', 'decoder', 'audioDecoder', 'codec',
                   'fps', 'width', 'height', 'bitrate', 'dropped', 'network', 'error')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--serial', required=True)
    parser.add_argument('--adb', default='adb')
    parser.add_argument('--cases', type=Path, required=True)
    parser.add_argument('--mode', choices=('baseline', 'diagnostics', 'read-ahead'), required=True)
    parser.add_argument('--seconds', type=int, default=600)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    cases = json.loads(args.cases.read_text(encoding='utf-8-sig'))
    if args.seconds <= 0 or not cases:
        parser.error('Use a positive duration and at least one case')
    for case in cases:
        if not re.fullmatch(r'[A-Za-z0-9_-]+', case['id']):
            parser.error('Use anonymous alphanumeric case IDs')
    output = args.output or ROOT / 'Docs/Validation' / ('smb-playback-' + args.mode + '.json')
    output.parent.mkdir(parents=True, exist_ok=True)
    backup = output.with_suffix('.settings.backup.json')
    command_file = ROOT / 'TestMedia/smb-playback-command.json'
    command_file.parent.mkdir(parents=True, exist_ok=True)
    stop_wake = threading.Event()
    report = {'mode': args.mode, 'requestedSeconds': args.seconds, 'started': time.strftime('%Y-%m-%d %H:%M:%S'),
              'runs': [], 'settingsRestored': False}

    def adb(*values, check=True):
        process = subprocess.run([args.adb, '-s', args.serial, *values], capture_output=True, timeout=30)
        if check and process.returncode:
            raise RuntimeError('ADB command failed (details withheld to protect media paths)')
        return process.stdout

    def status():
        # The existing Unity snapshot writer replaces the file in place. A concurrent
        # read can see an empty/partial document; retry briefly, then fail normally.
        for attempt in range(6):
            try:
                return json.loads(adb('exec-out', 'cat', EXTERNAL + 'playback-diagnostics.json'))
            except json.JSONDecodeError:
                if attempt == 5:
                    raise RuntimeError('Device diagnostics remained incomplete') from None
                time.sleep(.1)

    def wait(read, predicate, timeout=45):
        deadline = time.monotonic() + timeout
        while time.monotonic() < deadline:
            try:
                value = read()
                if predicate(value):
                    return value
            except (ValueError, KeyError, RuntimeError):
                pass
            time.sleep(.25)
        raise RuntimeError('Timed out waiting for fresh device state; keep the headset awake')

    def command(action, **values):
        command_file.write_text(json.dumps({'action': action, **values}), encoding='utf-8')
        adb('shell', 'run-as', PACKAGE, 'rm', '-f', 'files/command-result.json')
        adb('push', str(command_file), '/data/local/tmp/vrplayer-smb-test.json')
        adb('shell', 'run-as', PACKAGE, 'cp', '/data/local/tmp/vrplayer-smb-test.json', 'files/command.tmp')
        adb('shell', 'run-as', PACKAGE, 'mv', 'files/command.tmp', 'files/command.json')
        return wait(lambda: json.loads(adb('shell', 'run-as', PACKAGE, 'cat', 'files/command-result.json')),
                    lambda item: item.get('accepted') and item.get('action') == action)

    def save():
        output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')

    def memory():
        text = adb('shell', 'dumpsys', 'meminfo', PACKAGE).decode(errors='replace')
        match = re.search(r'^\s*TOTAL\s+(\d+)\s', text, re.M)
        return int(match.group(1)) if match else None

    def wake():
        while not stop_wake.wait(1):
            adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP', check=False)

    backed_up = False
    power_settings = {}
    try:
        installed = adb('shell', 'pm', 'path', PACKAGE).decode().strip().splitlines()[0].removeprefix('package:')
        report['apkSha256'] = adb('shell', 'sha256sum', installed).decode().split()[0]
        adb('shell', 'am', 'force-stop', PACKAGE)
        original = adb('exec-out', 'cat', EXTERNAL + 'settings.json')
        backup.write_bytes(original)
        backed_up = True
        # PICO may store -1 for screen_off_timeout, which Android's shell cannot restore.
        # Leave that vendor setting untouched; USB stay-awake is independently reversible.
        for namespace, key in (('global', 'stay_on_while_plugged_in'),):
            power_settings[(namespace, key)] = adb('shell', 'settings', 'get', namespace, key).decode().strip()
        output.with_suffix('.power.backup.json').write_text(json.dumps([
            {'namespace': namespace, 'key': key, 'value': value}
            for (namespace, key), value in power_settings.items()]), encoding='utf-8')
        adb('shell', 'svc', 'power', 'stayon', 'true')
        settings = json.loads(original)
        settings.update(speed=1, loop=False, sequential=False, autoLoadSubtitles=False,
                        formatDetection=0, autoHide=False, renderQuality=1)
        temporary = output.with_suffix('.test-settings.json')
        temporary.write_text(json.dumps(settings, ensure_ascii=False), encoding='utf-8')
        adb('push', str(temporary), EXTERNAL + 'settings.json')
        threading.Thread(target=wake, daemon=True).start()
        adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
        adb('shell', 'monkey', '-p', PACKAGE, '-c', 'android.intent.category.LAUNCHER', '1')
        wait(status, lambda item: item['snapshot']['generation'] == 0)
        if args.mode != 'baseline':
            command('smb-read-ahead', flag=args.mode == 'read-ahead')
        for case in cases:
            previous = status()['snapshot']['generation']
            command('repeat', flag=case.get('loop', False))
            command('open', uri=case['uri'], name=case['id'])
            wait(status, lambda item: item['snapshot']['generation'] != previous and item['snapshot']['state'] == 3)
            command('projection', projection=0, stereo=0)
            command('seek', position=case.get('startMs', 60000))
            command('play')
            wait(status, lambda item: item['snapshot']['playing'] and abs(item['snapshot']['position']-case.get('startMs', 60000)) < 10000)
            command('tab', name='信息')
            run = {'id': case['id'], 'source': case['uri'].split(':', 1)[0], 'samples': [], 'memory': []}
            report['runs'].append(run)
            started = time.monotonic()
            next_progress = 0
            last_fresh = started
            last_identity = None
            while time.monotonic() - started < args.seconds:
                value = status()
                state = value['snapshot']
                elapsed = time.monotonic() - started
                identity = (state.get('monotonicMs'), state['position'], value['frames'], state['state'])
                if identity != last_identity:
                    last_fresh, last_identity = time.monotonic(), identity
                if time.monotonic() - last_fresh > 12:
                    raise RuntimeError('Device diagnostics stopped updating; run is not valid')
                sample = {key: state[key] for key in SNAPSHOT_FIELDS if key in state}
                sample.update(elapsed=round(elapsed, 2), frames=value['frames'], unityFps=value['unityFps'])
                run['samples'].append(sample)
                if state.get('error') or state['state'] == 4:
                    raise RuntimeError('Playback error or unexpected end of media')
                if state['state'] == 3 and not state['playing']:
                    raise RuntimeError('Playback paused externally; run is not valid')
                if elapsed >= next_progress:
                    run['memory'].append({'elapsed': round(elapsed, 1), 'pssKB': memory()})
                    print(f"{args.mode} {case['id']}: {elapsed:.0f}s, state={state['state']}, rebuffer={state.get('rebufferCount', 'unavailable')}, reads={(state.get('network') or {}).get('requests', 'unavailable')}", flush=True)
                    next_progress += 30
                    save()
                time.sleep(.5)
            run['completedSeconds'] = round(time.monotonic()-started, 2)
            samples = run['samples']
            run['summary'] = {'sampledBufferingStates': sum(item['state'] == 2 for item in samples),
                              'frameDelta': samples[-1]['frames']-samples[0]['frames'],
                              'rebufferCountDelta': samples[-1]['rebufferCount']-samples[0]['rebufferCount'] if 'rebufferCount' in samples[-1] else None,
                              'rebufferMsDelta': samples[-1]['rebufferMs']-samples[0]['rebufferMs'] if 'rebufferMs' in samples[-1] else None}
            run['uninterrupted'] = run['summary']['sampledBufferingStates'] == 0 and run['summary']['rebufferCountDelta'] == 0
            run['completed'] = True
            save()
        report['completed'] = True
    except Exception as exc:
        report['completed'] = False
        report['failure'] = str(exc)
        raise
    finally:
        stop_wake.set()
        cleanup_errors = []
        try:
            adb('shell', 'am', 'force-stop', PACKAGE)
        except Exception:
            cleanup_errors.append('Could not stop the player before restoring settings')
        if args.mode != 'baseline':
            try:
                for name in ('playback-events.jsonl', 'playback-events.previous.jsonl'):
                    adb('pull', EXTERNAL + name, str(output.with_suffix('.' + name)), check=False)
            except Exception:
                report['telemetryPullFailed'] = True
        try:
            if backed_up:
                adb('push', str(backup), EXTERNAL + 'settings.json')
                restored = adb('exec-out', 'cat', EXTERNAL + 'settings.json')
                report['settingsRestored'] = hashlib.sha256(restored).digest() == hashlib.sha256(backup.read_bytes()).digest()
        except Exception:
            cleanup_errors.append('Could not restore player settings; retain the backup')
        try:
            for (namespace, key), value in power_settings.items():
                if value == 'null':
                    adb('shell', 'settings', 'delete', namespace, key)
                else:
                    adb('shell', 'settings', 'put', namespace, key, value)
            report['powerSettingsRestored'] = all(adb('shell', 'settings', 'get', namespace, key).decode().strip() == value
                                                 for (namespace, key), value in power_settings.items())
        except Exception:
            report['powerSettingsRestored'] = False
            cleanup_errors.append('Could not restore USB stay-awake setting; retain the backup')
        finally:
            report['cleanupErrors'] = cleanup_errors
            save()
        if cleanup_errors or (backed_up and not report['settingsRestored']) or not report.get('powerSettingsRestored'):
            raise RuntimeError('Original device settings were not restored; see report and backups')


if __name__ == '__main__':
    main()
