"""Check adjacent-button format inheritance on PICO using generated local fixtures.

Requires Recognition clips from Generate-RecognitionMedia.py on the device.
Original settings are backed up to ignored validation storage and restored byte-for-byte.
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
FIXTURES = '/sdcard/Movies/VRPlayerTest/Recognition/'
FOLDER = '/sdcard/Movies/VRPlayerTest/Adjacent/'
MEDIA = 'file://' + FOLDER
NAMES = ['A.360.OU.mp4', 'B.180.SBS.mp4', 'C.mp4', 'D.mp4']
parser = argparse.ArgumentParser()
parser.add_argument('--serial', required=True)
parser.add_argument('--capture', action='store_true', help='Also capture the projection page on a generated unmarked fixture.')
parser.add_argument('--adb', default=r'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe')
args = parser.parse_args()
report = {'started': time.strftime('%Y-%m-%d %H:%M:%S'), 'checks': [], 'passed': False}
output = ROOT / 'Docs/Validation/adjacent-device.json'
backup = ROOT / 'Docs/Validation/adjacent-device-settings.backup.json'
temporary = ROOT / 'Docs/Validation/adjacent-fixture-settings.json'
awake = threading.Event()


def adb(*command):
    return subprocess.run([args.adb, '-s', args.serial, *command], capture_output=True, check=True).stdout


def wake():
    while not awake.wait(1):
        try:
            adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
        except subprocess.CalledProcessError:
            pass


def wait(read, predicate, timeout=30):
    until = time.monotonic() + timeout
    while time.monotonic() < until:
        try:
            value = read()
            if predicate(value):
                return value
        except (json.JSONDecodeError, KeyError, subprocess.CalledProcessError):
            pass
        time.sleep(.4)
    raise AssertionError('Expected generated fixture state was not reached')


def command(action, **fields):
    temporary.write_text(json.dumps({'action': action, **fields}), encoding='utf-8')
    adb('shell', 'run-as', PACKAGE, 'rm', '-f', 'files/command-result.json')
    adb('push', str(temporary), '/data/local/tmp/vrplayer-adjacent.json')
    adb('shell', 'run-as', PACKAGE, 'cp', '/data/local/tmp/vrplayer-adjacent.json', 'files/command.tmp')
    adb('shell', 'run-as', PACKAGE, 'mv', 'files/command.tmp', 'files/command.json')
    return wait(lambda: json.loads(adb('shell', 'run-as', PACKAGE, 'cat', 'files/command-result.json')), lambda r: r.get('accepted') and r.get('action') == action)


def status():
    return json.loads(adb('shell', 'cat', EXTERNAL + 'playback-diagnostics.json'))


def switch(action, name=None):
    old = status()['snapshot']['generation']
    command(action, **({'uri': MEDIA + name, 'name': name} if name else {}))
    result = wait(status, lambda r: r['snapshot']['generation'] > old and r['snapshot']['state'] == 3)
    assert not result['snapshot']['error']
    return result


def current():
    return json.loads(adb('shell', 'cat', EXTERNAL + 'settings.json'))['history'][0]


def passed(name):
    report['checks'].append({'name': name, 'passed': True})
    print('PASS ' + name, flush=True)


backup_created = False
try:
    output.parent.mkdir(parents=True, exist_ok=True)
    adb('shell', 'am', 'force-stop', PACKAGE)
    adb('pull', EXTERNAL + 'settings.json', str(backup))
    backup_created = True
    settings = json.loads(backup.read_text(encoding='utf-8'))
    settings.update(version=3, autoLoadSubtitles=False, formatDetection=2, loop=True, sequential=False)
    settings['history'] = []
    for i, name in enumerate(NAMES):
        settings['history'].append({'uri': MEDIA + name, 'name': name, 'directory': MEDIA, 'formatLocked': True,
            'subtitleSelection': 2, 'subtitle': '', 'audio': 'auto', 'position': 0,
            'view': {'projection': 3 if i == 0 else 1, 'stereo': 2 if i == 0 else 0, 'half': i != 0,
                     'swapEyes': i == 0, 'fisheyeFov': 210 if i == 0 else 180,
                     'distance': 2 if i == 0 else 7, 'width': 4.5, 'brightness': 1, 'monoEye': i != 0}})
    temporary.write_text(json.dumps(settings), encoding='utf-8')
    adb('push', str(temporary), EXTERNAL + 'settings.json')
    adb('shell', 'mkdir', '-p', FOLDER)
    for source, name in zip(['Unmarked-A.mp4', 'Unmarked-B.mp4', 'Plain-Wide.mp4', 'Plain-Wide.mp4'], NAMES):
        adb('shell', 'cp', FIXTURES + source, FOLDER + name)
    threading.Thread(target=wake, daemon=True).start()
    adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
    adb('shell', 'monkey', '-p', PACKAGE, '-c', 'android.intent.category.LAUNCHER', '1')
    wait(status, lambda r: r['snapshot']['generation'] == 0)
    command('source', name='recent')
    switch('open', NAMES[0])
    switch('next')
    target = current()
    assert target['uri'] == MEDIA + NAMES[1] and target['formatLocked']
    view = target['view']
    assert (view['projection'], view['stereo'], view['half'], view['swapEyes'], view['fisheyeFov']) == (3, 2, False, True, 210)
    assert view['distance'] == 7 and view['monoEye'] and target['subtitleSelection'] == 2
    passed('Next from history uses actual siblings and inherits only source format')
    command('play')
    time.sleep(8)
    assert status()['projection'] == 'Fisheye' and status()['stereo'] == 'TopBottom'
    passed('Enhanced detection does not overwrite inherited format during playback')
    command('browse', uri='file://' + FIXTURES)
    wait(status, lambda r: r['directory'] == 'file://' + FIXTURES)
    switch('next')
    assert current()['uri'] == MEDIA + NAMES[2] and current()['view']['projection'] == 3
    passed('Browsing another directory does not change the playback playlist')
    command('projection', projection=2, stereo=1)
    switch('previous')
    assert current()['uri'] == MEDIA + NAMES[1] and current()['view']['projection'] == 2 and current()['view']['stereo'] == 1
    passed('Previous inherits the most recent format choice')
    command('detect')
    wait(status, lambda r: r['projection'] == 'Equirect180' and r['stereo'] == 'SideBySide')
    passed('Explicit redetection unlocks inherited format')
    command('detect-undo')
    wait(status, lambda r: r['projection'] == 'Equirect360' and r['stereo'] == 'SideBySide')
    passed('Undo redetection restores the inherited format')
    result = switch('open', NAMES[3])
    assert result['projection'] == 'Equirect180' and result['stereo'] == 'Mono'
    passed('Ordinary file opening preserves its own saved format')
    generation = result['snapshot']['generation']
    command('next')
    time.sleep(2.5)
    assert status()['snapshot']['generation'] == generation
    passed('Last item does not wrap or overwrite another record')
    if args.capture:
        command('open', uri='file://' + FIXTURES + 'Unmarked-A.mp4', name='Unmarked-A.mp4')
        command('detect')
        command('play')
        wait(status, lambda r: r['stereo'] == 'SideBySide')
        command('tab', name='投影')
        command('menu', flag=True)
        time.sleep(1)
        capture = ROOT / 'Docs/Captures/vr180-projection.png'
        capture.parent.mkdir(parents=True, exist_ok=True)
        adb('shell', 'screencap', '-p', '/sdcard/vrplayer-projection.png')
        adb('pull', '/sdcard/vrplayer-projection.png', str(capture))
    report['passed'] = True
finally:
    awake.set()
    report['finished'] = time.strftime('%Y-%m-%d %H:%M:%S')
    output.write_text(json.dumps(report, indent=2), encoding='utf-8')
    if backup_created:
        adb('shell', 'am', 'force-stop', PACKAGE)
        adb('push', str(backup), EXTERNAL + 'settings.json')
        assert adb('exec-out', 'cat', EXTERNAL + 'settings.json') == backup.read_bytes(), 'Original settings were not restored'
        print('Original user settings restored.', flush=True)
