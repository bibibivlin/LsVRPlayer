"""Measure paired 8K recognition overhead, rapid switching and background recovery.

Uses generated fixtures only; restores the original settings and does not print them.
Short development-APK measurements are not a thermal or subjective headset acceptance.
"""
import argparse
import json
from pathlib import Path
import statistics
import subprocess
import threading
import time

ROOT = Path(__file__).resolve().parent.parent
PACKAGE = 'com.linxyi.LsVRPlayer'
EXTERNAL = '/sdcard/Android/data/' + PACKAGE + '/files/'
MEDIA = 'file:///sdcard/Movies/VRPlayerTest/'
parser = argparse.ArgumentParser()
parser.add_argument('--serial', required=True)
parser.add_argument('--adb', default=r'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe')
args = parser.parse_args()
report_path = ROOT / 'Docs/Validation/recognition-stress.json'
backup_path = ROOT / 'Docs/Validation/recognition-stress-settings.backup.json'
report = {'started': time.strftime('%Y-%m-%d %H:%M:%S'), 'checks': [], 'runs': []}
awake = threading.Event()


def adb(*values, check=True):
    return subprocess.run([args.adb, '-s', args.serial, *values], capture_output=True, check=check).stdout.decode('utf-8', errors='replace')


def wait(read, predicate, timeout=40):
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        try:
            value = read()
            if predicate(value):
                return value
        except (json.JSONDecodeError, subprocess.CalledProcessError, KeyError):
            pass
        time.sleep(.3)
    raise AssertionError('Timed out waiting for generated fixture state')


def status():
    return json.loads(adb('shell', 'cat', EXTERNAL + 'playback-diagnostics.json'))


def command(action, **values):
    path = ROOT / 'TestMedia/stress-command.json'
    path.write_text(json.dumps({'action': action, **values}), encoding='utf-8')
    adb('shell', 'run-as', PACKAGE, 'rm', '-f', 'files/command-result.json')
    adb('push', str(path), '/data/local/tmp/vrplayer-stress.json')
    adb('shell', 'run-as', PACKAGE, 'cp', '/data/local/tmp/vrplayer-stress.json', 'files/command.tmp')
    adb('shell', 'run-as', PACKAGE, 'mv', 'files/command.tmp', 'files/command.json')
    return wait(lambda: json.loads(adb('shell', 'run-as', PACKAGE, 'cat', 'files/command-result.json')), lambda result: result.get('accepted') and result.get('action') == action)


def open_media(name):
    previous = status()['snapshot']['generation']
    command('open', uri=MEDIA + name, name=name.split('/')[-1])
    value = wait(status, lambda item: item['snapshot']['generation'] != previous and item['snapshot']['state'] == 3)
    assert not value['snapshot']['error']
    command('seek', position=0)
    command('play')
    return value


def save():
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')


def record(name, evidence):
    report['checks'].append({'name': name, 'passed': True, 'evidence': evidence})
    save()
    print('PASS ' + name, flush=True)


def wake_loop():
    while not awake.wait(1):
        adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP', check=False)


def foreground():
    adb('shell', 'monkey', '-p', PACKAGE, '-c', 'android.intent.category.LAUNCHER', '1')


backup_created = False
try:
    adb('shell', 'am', 'force-stop', PACKAGE)
    backup_created = subprocess.run([args.adb, '-s', args.serial, 'pull', EXTERNAL + 'settings.json', str(backup_path)], capture_output=True).returncode == 0
    threading.Thread(target=wake_loop, daemon=True).start()
    adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
    foreground()
    wait(status, lambda value: value['snapshot']['generation'] == 0)
    command('repeat', flag=True)
    command('subtitle-auto', flag=False)
    command('quality', preset=1)
    # Balanced order reduces drift from warmup. Each run samples the early analysis window.
    for mode in [0, 2, 2, 0]:
        open_media('Performance.8K30.HEVC.mp4')
        command('detection-mode', preset=mode)
        command('detect')  # Unlock a migrated history entry; mode=0 has no frame sampling.
        command('detection-mode', preset=mode)  # Off now also disables the explicit probe.
        command('menu', flag=False)
        command('play')
        samples = []
        interruptions = 0
        began = time.monotonic()
        while time.monotonic() - began < 12:
            state = status()
            assert state['snapshot']['width'] == 7680 and not state['snapshot']['error']
            if not state['snapshot']['playing']:
                interruptions += 1
                command('play')
            elif state['unityFps'] > 0:
                samples.append({'seconds': time.monotonic() - began, 'fps': state['unityFps'], 'recognitionP95Ms': state['recognitionP95'], 'frames': state['frames']})
            time.sleep(.5)
        assert len(samples) >= 8 and samples[-1]['frames'] > samples[0]['frames'] + 30
        run = {'mode': mode, 'meanFps': statistics.mean(item['fps'] for item in samples), 'p95Ms': max(item['recognitionP95Ms'] for item in samples), 'interruptions': interruptions, 'samples': samples}
        report['runs'].append(run)
        record('8K playback with detection mode ' + str(mode), run)
    baseline = statistics.mean(run['meanFps'] for run in report['runs'] if run['mode'] == 0)
    enhanced = statistics.mean(run['meanFps'] for run in report['runs'] if run['mode'] == 2)
    report['performance'] = {'baselineFps': baseline, 'enhancedFps': enhanced, 'fpsDropPercent': (baseline - enhanced) / baseline * 100, 'p95Ms': max(run['p95Ms'] for run in report['runs'] if run['mode'] == 2), 'uninterrupted': all(run['interruptions'] == 0 for run in report['runs'])}
    command('detection-mode', preset=2)
    command('subtitle-auto', flag=True)
    # Do not wait for old subtitle or frame-analysis callbacks before opening the next movie.
    for index in range(12):
        name = ['Recognition/Unmarked-A.mp4', 'Recognition/Unmarked-B.mp4', 'Recognition/Plain-Wide.mp4'][index % 3]
        command('open', uri=MEDIA + name, name=name.split('/')[-1])
    open_media('Recognition/Unmarked-B.mp4')
    command('subtitle-reset')
    final = wait(status, lambda value: value['subtitle'].endswith('/Unmarked-B.srt'))
    record('Rapid switching discards obsolete subtitle and analysis results', final)
    command('pause')
    command('seek', position=4000)
    before = wait(status, lambda value: abs(value['snapshot']['position'] - 4000) < 500)
    adb('shell', 'input', 'keyevent', 'KEYCODE_HOME')
    time.sleep(4)
    foreground()
    after = wait(status, lambda value: not value['snapshot']['playing'] and abs(value['snapshot']['position'] - 4000) < 500)
    record('Background recovery retains paused seek position', {'before': before, 'after': after})
    command('play')
    record('Playback resumes after background recovery', wait(status, lambda value: value['snapshot']['playing'] and value['snapshot']['position'] > 4500))
    command('pause')
    command('seek', position=5000)
    wait(status, lambda value: abs(value['snapshot']['position'] - 5000) < 500)
    command('stop')
    adb('shell', 'am', 'force-stop', PACKAGE)
    foreground()
    wait(status, lambda value: value['snapshot']['generation'] == 0)
    command('open', uri=MEDIA + 'Recognition/Unmarked-B.mp4', name='Unmarked-B.mp4')
    record('Process restart restores position and automatic subtitle state', wait(status, lambda value: 4500 <= value['snapshot']['position'] <= 9500 and value['subtitle'].endswith('/Unmarked-B.srt')))
    report['passed'] = True
except Exception as error:
    report['passed'] = False
    report['error'] = str(error)
    raise
finally:
    awake.set()
    report['finished'] = time.strftime('%Y-%m-%d %H:%M:%S')
    save()
    if backup_created:
        adb('shell', 'am', 'force-stop', PACKAGE, check=False)
        adb('push', str(backup_path), EXTERNAL + 'settings.json', check=False)
        restored = subprocess.run([args.adb, '-s', args.serial, 'exec-out', 'cat', EXTERNAL + 'settings.json'], capture_output=True, check=True).stdout
        assert restored == backup_path.read_bytes(), 'Original settings could not be restored'
        print('Original user settings restored.', flush=True)
