"""Regression checks for automatic SRT, history directory and format recognition on one PICO.

Uses only generated fixtures. Settings backup stays in ignored Docs/Validation and is restored.
The optional --host uses the real LAN (no adb reverse).
"""
import argparse
import json
from pathlib import Path
import subprocess
import time
import threading

ROOT = Path(__file__).resolve().parent.parent
PACKAGE = 'com.linxyi.LsVRPlayer'
EXTERNAL = '/sdcard/Android/data/' + PACKAGE + '/files/'
MEDIA = 'file:///sdcard/Movies/VRPlayerTest/Recognition/'
parser = argparse.ArgumentParser()
parser.add_argument('--serial', required=True)
parser.add_argument('--host', default='')
parser.add_argument('--adb', default=r'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe')
parser.add_argument('--skip-settings-restore', action='store_true', help='For development iteration only; default restores settings.')
args = parser.parse_args()
report_path = ROOT / 'Docs/Validation/improvements-device.json'
backup_path = ROOT / 'Docs/Validation/improvements-device-settings.backup.json'
report = {'started': time.strftime('%Y-%m-%d %H:%M:%S'), 'device': args.serial, 'checks': [], 'limitations': ['Hardware joystick feel and binocular comfort require wearing the headset.']}


def adb(*command, check=True):
    return subprocess.run([args.adb, '-s', args.serial, *command], capture_output=True, check=check).stdout.decode('utf-8', errors='replace')


def wait(read, predicate, timeout=30):
    until = time.monotonic() + timeout
    while time.monotonic() < until:
        try:
            value = read()
            if predicate(value):
                return value
        except (json.JSONDecodeError, subprocess.CalledProcessError, KeyError):
            pass
        time.sleep(.4)
    raise AssertionError('Timed out waiting for the expected fixture state')


def command(action, **fields):
    adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
    path = ROOT / 'TestMedia/improvements-command.json'
    path.write_text(json.dumps({'action': action, **fields}, ensure_ascii=False), encoding='utf-8')
    adb('shell', 'run-as', PACKAGE, 'rm', '-f', 'files/command-result.json')
    adb('push', str(path), '/data/local/tmp/vrplayer-improvements.json')
    adb('shell', 'run-as', PACKAGE, 'cp', '/data/local/tmp/vrplayer-improvements.json', 'files/command.tmp')
    adb('shell', 'run-as', PACKAGE, 'mv', 'files/command.tmp', 'files/command.json')
    return wait(lambda: json.loads(adb('shell', 'run-as', PACKAGE, 'cat', 'files/command-result.json')), lambda value: value.get('accepted') and value.get('action') == action)


def status():
    adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
    return json.loads(adb('shell', 'cat', EXTERNAL + 'playback-diagnostics.json'))


def active_status():
    state = status()
    if state['snapshot']['state'] == 3 and not state['snapshot']['playing']:
        command('play')
    return state


def active_delay(seconds):
    until = time.monotonic() + seconds
    while time.monotonic() < until:
        active_status()
        time.sleep(.5)


def open_media(name, base=MEDIA):
    old = status()['snapshot']['generation']
    command('open', uri=base + name, name=name)
    state = wait(status, lambda value: value['snapshot']['generation'] != old and value['snapshot']['state'] == 3)
    if state['snapshot']['error']:
        raise AssertionError('Fixture failed to decode: ' + name)
    command('play')
    return state


def record(name, state=None):
    report['checks'].append({'name': name, 'passed': True, 'evidence': state})
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print('PASS ' + name, flush=True)


def capture(name):
    destination = ROOT / 'Docs/Captures' / name
    destination.parent.mkdir(parents=True, exist_ok=True)
    command('menu', flag=True)
    adb('shell', 'screencap', '-p', '/sdcard/vrplayer-improvements.png')
    adb('pull', '/sdcard/vrplayer-improvements.png', str(destination))


backup_created = False
smb_backup = None
keep_awake = threading.Event()
def wake_loop():
    while not keep_awake.wait(1):
        adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP', check=False)
threading.Thread(target=wake_loop, daemon=True).start()
try:
    adb('shell', 'am', 'force-stop', PACKAGE)
    # Never print this file: it may contain private history paths.
    backup = subprocess.run([args.adb, '-s', args.serial, 'pull', EXTERNAL + 'settings.json', str(backup_path)], capture_output=True)
    backup_created = backup.returncode == 0
    if backup_created:
        fixture_settings = json.loads(backup_path.read_text(encoding='utf-8'))
        fixture_settings['history'] = []  # Keep screenshots confined to generated fixture history.
        isolated = ROOT / 'Docs/Validation/improvements-fixture-settings.json'
        isolated.write_text(json.dumps(fixture_settings, ensure_ascii=False), encoding='utf-8')
        adb('push', str(isolated), EXTERNAL + 'settings.json')
    if args.host:
        saved = subprocess.run([args.adb, '-s', args.serial, 'exec-out', 'run-as', PACKAGE, 'cat', 'shared_prefs/smb.xml'], capture_output=True)
        smb_backup = saved.stdout if saved.returncode == 0 else b''
        (ROOT / 'Docs/Validation/improvements-smb-settings.backup.xml').write_bytes(smb_backup)
    adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
    adb('shell', 'monkey', '-p', PACKAGE, '-c', 'android.intent.category.LAUNCHER', '1')
    wait(status, lambda value: value['snapshot']['generation'] == 0)
    command('repeat', flag=True)
    command('subtitle-auto', flag=True)
    open_media('Unmarked-A.mp4')
    record('Chinese suffix is automatically loaded', wait(status, lambda value: value['subtitle'].endswith('.chs.srt')))
    command('subtitle-off')
    open_media('Unmarked-A.mp4')
    record('Per-film subtitle off persists after reopen', wait(status, lambda value: value['subtitleSelection'] == 'Off' and not value['subtitle']))
    command('subtitle', uri=MEDIA + 'Unmarked-A.en.srt')
    open_media('Unmarked-A.mp4')
    record('Manual subtitle overrides automatic language priority', wait(status, lambda value: value['subtitle'].endswith('.en.srt')))
    command('subtitle-reset')
    record('Restore automatic subtitle selection', wait(status, lambda value: value['subtitle'].endswith('.chs.srt')))
    open_media('Unmarked-B.mp4')
    record('Broken Chinese SRT falls back to bare filename', wait(status, lambda value: value['subtitle'].endswith('/Unmarked-B.srt')))
    generation = status()['snapshot']['generation']
    command('source', name='recent')
    command('locate', uri=MEDIA + 'Unmarked-B.mp4')
    state = wait(status, lambda value: value['source'] == 'Directory' and value['directory'].rstrip('/').endswith('/Recognition'))
    assert state['snapshot']['generation'] == generation
    record('History opens containing directory without changing playback', state)
    command('pause')
    command('seek', position=1000)
    wait(status, lambda value: abs(value['snapshot']['position'] - 1000) < 500)
    command('skip', value=10)
    state = wait(status, lambda value: abs(value['snapshot']['position'] - 11000) < 500)
    assert not state['snapshot']['playing']
    record('Relative seek preserves paused state', state)
    command('skip', value=-10)
    record('Relative rewind', wait(status, lambda value: abs(value['snapshot']['position'] - 1000) < 500))
    command('detection-mode', preset=2)
    for name, expected in [('Unmarked-A.mp4', 'SideBySide'), ('Unmarked-B.mp4', 'TopBottom')]:
        open_media(name)
        command('detect')
        record('Frame detection ' + name, wait(active_status, lambda value: value['stereo'] == expected, 25))
    for name in ['Plain-Wide.mp4', 'Split-Screen.mp4', 'Mirror.mp4']:
        open_media(name)
        command('detect')
        active_delay(10)
        state = status()
        assert state['stereo'] == 'Mono' and state['projection'] == 'Flat', name
        record('No false automatic VR/stereo: ' + name, state)
    for name, projection in [('Grid.180.SBS.mp4', 'Equirect180'), ('Grid.360.mp4', 'Equirect360'), ('Grid.Fisheye.mp4', 'Fisheye')]:
        open_media(name)
        command('detect')
        record('Filename projection ' + name, wait(status, lambda value: value['projection'] == projection))
    open_media('Unmarked-Metadata.mp4')
    record('MP4 projection metadata without filename tags', wait(status, lambda value: value['projection'] == 'Equirect360' and value['stereo'] == 'Mono'))
    open_media('Container-B.mkv')
    record('Matroska stereo metadata without filename tags', wait(active_status, lambda value: value['stereo'] == 'TopBottom'))
    open_media('Black-Intro.mp4')
    command('detect')
    record('Frame recognition waits through black introduction', wait(active_status, lambda value: value['stereo'] == 'SideBySide'))
    open_media('Unmarked-A.mp4')
    command('projection', projection=2, stereo=2)
    open_media('Unmarked-A.mp4')
    active_delay(5)
    state = status()
    assert state['projection'] == 'Equirect360' and state['stereo'] == 'TopBottom'
    record('Saved manual projection is not overwritten', state)
    command('detect')
    wait(active_status, lambda value: value['stereo'] == 'SideBySide')
    command('detect-undo')
    record('Undo restores prior format', wait(status, lambda value: value['projection'] == 'Equirect360' and value['stereo'] == 'TopBottom'))
    command('tab', name='设置')
    capture('improvements-settings.png')
    command('tab', name='字幕')
    capture('improvements-subtitles.png')
    command('source', name='recent')
    command('tab', name='文件')
    capture('improvements-history.png')
    command('tab', name='投影')
    command('detect')
    wait(active_status, lambda value: value['stereo'] == 'SideBySide')
    capture('improvements-projection.png')
    if args.host:
        command('smb', name='Improvements fixture', host=args.host, share='TestMedia', port=1445)
        files = command('inspect')['files']
        server = next(item for item in files if item['name'] == 'Improvements fixture')
        base = server['uri'] + 'Recognition/'
        open_media('Unmarked-B.mp4', base)
        command('subtitle-reset')
        record('Real LAN SMB automatic subtitle fallback', wait(status, lambda value: value['subtitle'].startswith('smb:') and value['subtitle'].endswith('Unmarked-B.srt')))
        command('detect')
        record('Real LAN SMB frame recognition', wait(active_status, lambda value: value['stereo'] == 'TopBottom'))
        command('locate', uri=base + 'Unmarked-B.mp4')
        record('SMB history parent preserves configuration id', wait(status, lambda value: value['directory'] == base))
    report['passed'] = True
except Exception as error:
    report['passed'] = False
    report['error'] = str(error)
    print('FAIL ' + str(error), flush=True)
    raise
finally:
    keep_awake.set()
    report['finished'] = time.strftime('%Y-%m-%d %H:%M:%S')
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    if not args.skip_settings_restore and backup_created:
        adb('shell', 'am', 'force-stop', PACKAGE, check=False)
        adb('push', str(backup_path), EXTERNAL + 'settings.json', check=False)
        restored = subprocess.run([args.adb, '-s', args.serial, 'exec-out', 'cat', EXTERNAL + 'settings.json'], capture_output=True, check=True).stdout
        assert restored == backup_path.read_bytes(), 'Original settings could not be restored'
        if smb_backup is not None:
            if smb_backup:
                local = ROOT / 'Docs/Validation/improvements-smb-settings.backup.xml'
                adb('push', str(local), '/data/local/tmp/vrplayer-smb-restore.xml', check=False)
                adb('shell', 'run-as', PACKAGE, 'cp', '/data/local/tmp/vrplayer-smb-restore.xml', 'shared_prefs/smb.xml', check=False)
                restored_smb = subprocess.run([args.adb, '-s', args.serial, 'exec-out', 'run-as', PACKAGE, 'cat', 'shared_prefs/smb.xml'], capture_output=True, check=True).stdout
                assert restored_smb == smb_backup, 'Original SMB settings could not be restored'
            else:
                adb('shell', 'run-as', PACKAGE, 'rm', '-f', 'shared_prefs/smb.xml', check=False)
        print('Original user settings restored; backup remains ignored.', flush=True)
