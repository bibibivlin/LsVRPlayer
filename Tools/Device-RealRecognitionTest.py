"""Observe user-authorized local clips on PICO without tags, cache or directory preference.

Source media is read-only. Only anonymous IDs enter the report. Device settings are
restored byte-for-byte; the report contains candidate scores, not a precision claim.
"""
import argparse
import json
from pathlib import Path
import subprocess
import threading
import time

ROOT = Path(__file__).resolve().parent.parent
PACKAGE = 'com.linxyi.LsVRPlayer'
EXTERNAL = '/sdcard/Android/data/' + PACKAGE + '/files/'
FOLDER = '/sdcard/Movies/VRPlayerTest/RealRecognition/'
parser = argparse.ArgumentParser()
parser.add_argument('--source', required=True)
parser.add_argument('--serial', required=True)
parser.add_argument('--skip-transfer', action='store_true', help='Reuse anonymous device copies after checking their sizes.')
parser.add_argument('--adb', default=r'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe')
args = parser.parse_args()
output = ROOT / 'Docs/Validation/vr180-real-device.json'
backup = output.with_name('vr180-real-settings.backup.json')
temporary = ROOT / 'TestMedia/real-command.json'
report = {'clips': [], 'checks': [], 'dataset': 'User calibration clips; no independent accuracy claim'}
awake = threading.Event()


def adb(*values):
    return subprocess.run([args.adb, '-s', args.serial, *values], capture_output=True, check=True).stdout


def status():
    return json.loads(adb('shell', 'cat', EXTERNAL + 'playback-diagnostics.json'))


def wait(read, predicate, timeout=40):
    until = time.monotonic() + timeout
    while time.monotonic() < until:
        try:
            value = read()
            if predicate(value):
                return value
        except (json.JSONDecodeError, KeyError, subprocess.CalledProcessError):
            pass
        time.sleep(.3)
    raise AssertionError('Timed out waiting for test state')


def command(action, **fields):
    temporary.write_text(json.dumps({'action': action, **fields}), encoding='utf-8')
    adb('shell', 'run-as', PACKAGE, 'rm', '-f', 'files/command-result.json')
    adb('push', str(temporary), '/data/local/tmp/vrplayer-real.json')
    adb('shell', 'run-as', PACKAGE, 'cp', '/data/local/tmp/vrplayer-real.json', 'files/command.tmp')
    adb('shell', 'run-as', PACKAGE, 'mv', 'files/command.tmp', 'files/command.json')
    return wait(lambda: json.loads(adb('shell', 'run-as', PACKAGE, 'cat', 'files/command-result.json')), lambda r: r.get('accepted') and r.get('action') == action)


def wake():
    while not awake.wait(1):
        try:
            adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
        except subprocess.CalledProcessError:
            pass


def save():
    output.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')


backed_up = False
try:
    output.parent.mkdir(parents=True, exist_ok=True)
    files = sorted(p for p in Path(args.source).resolve().rglob('*') if p.suffix.lower() in ('.mp4', '.mkv', '.mov', '.webm'))
    assert files, 'No test clips'
    adb('shell', 'mkdir', '-p', FOLDER)
    for index, source in enumerate(files):
        device_path = FOLDER + f'sample-{index:03d}' + source.suffix.lower()
        if args.skip_transfer:
            assert int(adb('shell', 'stat', '-c', '%s', device_path)) == source.stat().st_size, 'Device copy size differs'
        else:
            adb('push', str(source), device_path)
        print(f'Transferred sample-{index:03d}', flush=True)
    adb('shell', 'am', 'force-stop', PACKAGE)
    adb('pull', EXTERNAL + 'settings.json', str(backup))
    backed_up = True
    settings = json.loads(backup.read_text(encoding='utf-8'))
    settings.update(version=3, history=[], vr180Directories=[], formatDetection=2, autoLoadSubtitles=False, loop=False, sequential=False)
    temporary.write_text(json.dumps(settings), encoding='utf-8')
    adb('push', str(temporary), EXTERNAL + 'settings.json')
    threading.Thread(target=wake, daemon=True).start()
    adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
    adb('shell', 'monkey', '-p', PACKAGE, '-c', 'android.intent.category.LAUNCHER', '1')
    wait(status, lambda r: r['snapshot']['generation'] == 0)
    command('quality', preset=1)
    for index, source in enumerate(files):
        name = f'sample-{index:03d}' + source.suffix.lower()
        old = status()['snapshot']['generation']
        command('open', uri='file://' + FOLDER + name, name=name)
        wait(status, lambda r: r['snapshot']['generation'] > old and r['snapshot']['state'] == 3)
        command('play')
        command('menu', flag=False)
        began = time.monotonic()
        row = {'id': f'sample-{index:03d}', 'observations': []}
        report['clips'].append(row)
        last_attempt = -1
        while time.monotonic() - began < 42:
            value = status()
            snapshot = value['snapshot']
            assert not snapshot['error'], 'Decoder error in anonymous clip'
            if value.get('analysisAttempts', 0) != last_attempt:
                last_attempt = value.get('analysisAttempts', 0)
                row['observations'].append({k: value.get(k) for k in ('projection', 'stereo', 'format', 'analysis', 'analysisStatus', 'analysisAttempts', 'analysisValidFrames', 'recognitionP95', 'unityFps')})
                row['observations'][-1].update(position=snapshot['position'], elapsed=time.monotonic() - began)
                save()
            if snapshot['state'] == 4 or snapshot['duration'] > 0 and snapshot['position'] >= snapshot['duration'] - 150:
                break
            if not snapshot['playing'] and snapshot['state'] == 3:
                command('play')
            time.sleep(.7)
        row['sbsApplied'] = any(r['stereo'] == 'SideBySide' for r in row['observations'])
        row['vr180Applied'] = any(r['projection'] == 'Equirect180' for r in row['observations'])
        row['vr180Suggested'] = any((r.get('format') or {}).get('suggestion', {}).get('projection') == 1 for r in row['observations'] if (r.get('format') or {}).get('suggestion'))
        print(f"sample-{index:03d}: SBS={row['sbsApplied']} VR180 applied={row['vr180Applied']} suggested={row['vr180Suggested']}", flush=True)
        save()
    # Explicit directory intent is tested separately and never counted as pure recognition.
    name = 'sample-000' + files[0].suffix.lower()
    command('open', uri='file://' + FOLDER + name, name=name)
    command('seek', position=5000)
    command('detect')
    command('play')
    wait(status, lambda r: r['stereo'] == 'SideBySide')
    command('directory-vr180', flag=True)
    wait(status, lambda r: r['projection'] == 'Equirect180')
    report['checks'].append('Explicit directory preference supplements confirmed SBS')
    command('directory-vr180', flag=False)
    wait(status, lambda r: r['projection'] == 'Flat')
    report['checks'].append('Removing directory preference removes its inferred projection')
    command('pause')
    command('projection', projection=3, stereo=1)
    before = wait(status, lambda r: not r['snapshot']['playing'] and r['projection'] == 'Fisheye')['snapshot']['position']
    time.sleep(3)
    after = status()
    assert after['projection'] == 'Fisheye' and not after['snapshot']['playing'] and abs(after['snapshot']['position'] - before) < 500
    report['checks'].append('Manual format cancels pending analysis and preserves pause')
    report['completed'] = True
finally:
    awake.set()
    save()
    if backed_up:
        adb('shell', 'am', 'force-stop', PACKAGE)
        adb('push', str(backup), EXTERNAL + 'settings.json')
        assert adb('exec-out', 'cat', EXTERNAL + 'settings.json') == backup.read_bytes(), 'Original settings were not restored'
        print('Original user settings restored.', flush=True)
