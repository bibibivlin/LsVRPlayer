"""Capture VR subtitle distance/style cases on an explicitly selected PICO development APK.

Requires generated Baseline fixtures on the device. Screenshots still need visual review.
"""
import argparse
import json
from pathlib import Path
import subprocess
import tempfile
import time

ROOT = Path(__file__).resolve().parent.parent
PACKAGE = 'com.linxyi.LsVRPlayer'
EXTERNAL = '/sdcard/Android/data/' + PACKAGE + '/files/'
parser = argparse.ArgumentParser()
parser.add_argument('--serial', required=True)
parser.add_argument('--adb', default=r'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe')
args = parser.parse_args()
report = []

def adb_result(*items, check=True):
    return subprocess.run([args.adb, '-s', args.serial, *items], capture_output=True, check=check)

def adb(*items):
    return adb_result(*items).stdout.decode('utf-8', errors='replace')

def read_settings(path):
    result = adb_result('shell', 'test', '-e', path, check=False)
    if result.returncode == 1 and not result.stderr:
        return None
    result.check_returncode()
    return adb_result('exec-out', 'cat', path).stdout

def restore_settings(saved, backup_path):
    adb('shell', 'am', 'force-stop', PACKAGE)
    failures = []
    for path, original in saved.items():
        name = Path(path).name
        try:
            if original is None:
                adb('shell', 'rm', '-f', path)
            else:
                adb('push', str(backup_path / name), path)
            if read_settings(path) != original:
                failures.append(name)
        except subprocess.CalledProcessError:
            failures.append(name)
    if failures:
        raise RuntimeError('Could not restore ' + ', '.join(failures))

def wait(read, predicate):
    deadline = time.monotonic() + 30
    last = None
    while time.monotonic() < deadline:
        try:
            last = read()
            if predicate(last):
                return last
        except (json.JSONDecodeError, subprocess.CalledProcessError):
            pass
        time.sleep(.3)
    raise AssertionError('Headset stopped updating or expectation failed: ' + str(last))

def command(action, **fields):
    adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
    payload = ROOT / 'TestMedia/caption-command.json'
    payload.write_text(json.dumps(dict(action=action, **fields)), encoding='utf-8')
    adb('shell', 'run-as', PACKAGE, 'rm', '-f', 'files/command-result.json')
    adb('push', str(payload), '/data/local/tmp/vrplayer-caption.json')
    adb('shell', 'run-as', PACKAGE, 'cp', '/data/local/tmp/vrplayer-caption.json', 'files/command.tmp')
    adb('shell', 'run-as', PACKAGE, 'mv', 'files/command.tmp', 'files/command.json')
    return wait(lambda: json.loads(adb('shell', 'run-as', PACKAGE, 'cat', 'files/command-result.json')), lambda r: r.get('accepted') and r.get('action') == action)

def status():
    return json.loads(adb('shell', 'cat', EXTERNAL + 'playback-diagnostics.json'))

def capture(name):
    adb('shell', 'screencap', '-p', '/sdcard/vrplayer-caption.png')
    target = ROOT / 'Docs/Captures' / (name + '.png')
    target.parent.mkdir(parents=True, exist_ok=True)
    adb('pull', '/sdcard/vrplayer-caption.png', str(target))
    return str(target.relative_to(ROOT))

saved_settings = {}
try:
    adb('shell', 'run-as', PACKAGE, 'id')
    saved = {EXTERNAL + name: read_settings(EXTERNAL + name) for name in ('settings.json', 'settings.json.tmp')}
    validation = ROOT / 'Docs/Validation'
    validation.mkdir(parents=True, exist_ok=True)
    backup_path = Path(tempfile.mkdtemp(prefix='caption-settings-', dir=validation))
    for path, original in saved.items():
        if original is not None:
            (backup_path / Path(path).name).write_bytes(original)
    saved_settings = saved
    report.append(dict(settingsBackup=str(backup_path.relative_to(ROOT))))
    command('quality', preset=1)
    previous = status()['snapshot']['generation']
    command('open', uri='file:///sdcard/Movies/VRPlayerTest/Baseline.2D.mp4', name='Baseline.2D.mp4')
    wait(status, lambda r: r['snapshot']['generation'] != previous and r['snapshot']['state'] == 3)
    command('pause')
    command('projection', projection=2, stereo=0)
    command('subtitle', uri='file:///sdcard/Movies/VRPlayerTest/Baseline.2D.chs.srt')
    command('seek', position=2000)
    wait(status, lambda r: r['projection'] == 'Equirect360' and '你好' in r['caption'])
    command('menu', flag=False)
    for distance in [.1, .3, 1, 3, 6]:
        command('caption-settings', distance=distance, size=42, offset=-.75, preset=1)
        time.sleep(.5)
        report.append(dict(distance=distance, screenshot=capture('caption-distance-' + str(distance)), state=status()))
        print('CAPTURE subtitle distance ' + str(distance), flush=True)
    for preset in range(4):
        command('caption-settings', distance=3, size=42, offset=-.75, preset=preset)
        time.sleep(.5)
        report.append(dict(preset=preset, screenshot=capture('caption-preset-' + str(preset)), state=status()))
        print('CAPTURE subtitle preset ' + str(preset), flush=True)
except Exception as error:
    report.append(dict(error=str(error)))
    raise
finally:
    try:
        if saved_settings:
            restore_settings(saved_settings, backup_path)
    except Exception as error:
        report.append(dict(restorationError=str(error)))
        raise
    finally:
        target = ROOT / 'Docs/Validation/caption-device.json'
        target.parent.mkdir(parents=True, exist_ok=True)
        target.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
