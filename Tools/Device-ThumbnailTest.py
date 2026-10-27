"""Run isolated PICO thumbnail decoder tests; no installed-player settings are changed."""
import argparse
import json
from pathlib import Path
import socket
import subprocess
import sys
import time
import uuid

parser = argparse.ArgumentParser()
parser.add_argument('--serial', required=True)
parser.add_argument('--smb', action='store_true', help='Include generated SMB fixture over adb reverse (not real LAN acceptance)')
parser.add_argument('--unity-editor', default=r'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor')
args = parser.parse_args()
root = Path(__file__).resolve().parent.parent
adb = Path(args.unity_editor) / 'Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe'
apk = root / 'Native/Android/build/outputs/apk/androidTest/debug/vrplayer-android-debug-androidTest.apk'
package = 'com.linxyi.vrplayer.test'
report = root / 'Docs/Validation/SeekThumbnails' / ('device-' + uuid.uuid4().hex[:12])
report.mkdir(parents=True)


def command(*parts, timeout=40):
    result = subprocess.run([str(adb), '-s', args.serial, *parts], capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=timeout)
    if result.returncode:
        raise RuntimeError(f'ADB {parts[0]} failed: {result.stdout} {result.stderr}')
    return result.stdout


if not apk.is_file():
    raise FileNotFoundError('Build Native/Android assembleDebugAndroidTest first')
if command('get-state').strip() != 'device':
    raise RuntimeError('Device unavailable')
if command('shell', 'pm', 'list', 'packages', package).strip():
    raise RuntimeError('Test package already exists; preserve it and use a separate clean test device/package')
install_attempted = reverse_owned = False
server = server_log = None
failure = None
cleanup_errors = []
try:
    if args.smb:
        # Check the port before starting a server, and never adopt or stop an unknown process.
        with socket.socket() as probe:
            probe.bind(('127.0.0.1', 1445))
        server_log = (report / 'smb-server.log').open('w', encoding='utf-8')
        server = subprocess.Popen([sys.executable, str(root / 'Tools/Serve-TestSmb.py')], cwd=root,
                                  stdout=server_log, stderr=subprocess.STDOUT,
                                  creationflags=getattr(subprocess, 'CREATE_NO_WINDOW', 0))
        deadline = time.monotonic() + 10
        while True:
            if server.poll() is not None:
                raise RuntimeError('Owned SMB server exited during startup')
            try:
                with socket.create_connection(('127.0.0.1', 1445), timeout=.3):
                    break
            except OSError:
                if time.monotonic() > deadline:
                    raise RuntimeError('SMB fixture did not start')
                time.sleep(.1)
        mappings = [line.split() for line in command('reverse', '--list').splitlines()]
        existing = [line for line in mappings if len(line) >= 3 and line[1] == 'tcp:1445']
        if existing and any(line[2] != 'tcp:1445' for line in existing):
            raise RuntimeError('Existing reverse mapping belongs to another task')
        if not existing:
            reverse_owned = True
            command('reverse', 'tcp:1445', 'tcp:1445')
    install_attempted = True
    result = command('install', str(apk), timeout=120)
    if 'Success' not in result:
        raise RuntimeError('Test package installation not confirmed')
    options = ['shell', 'am', 'instrument', '-w', '-r', '-e', 'class', 'com.linxyi.vrplayer.SeekThumbnailDeviceTest']
    if args.smb:
        options += ['-e', 'thumbnailSmbPort', '1445']
    result = command(*options, package + '/androidx.test.runner.AndroidJUnitRunner', timeout=180)
    (report / 'instrumentation.txt').write_text(result, encoding='utf-8')
    if 'FAILURES!!!' in result or 'OK (' not in result or 'INSTRUMENTATION_FAILED' in result:
        raise RuntimeError('Device regressions failed; see instrumentation.txt')
except Exception as error:
    failure = str(error)
finally:
    if install_attempted:
        try:
            exists = command('shell', 'pm', 'list', 'packages', package).strip()
            if exists and 'Success' not in command('uninstall', package, timeout=60):
                raise RuntimeError('Owned test package uninstall not confirmed')
            if command('shell', 'pm', 'list', 'packages', package).strip():
                raise RuntimeError('Owned test package remains installed')
        except Exception as error:
            cleanup_errors.append(str(error))
    if reverse_owned:
        try:
            command('reverse', '--remove', 'tcp:1445')
            if any(len(line.split()) >= 3 and line.split()[1] == 'tcp:1445' for line in command('reverse', '--list').splitlines()):
                raise RuntimeError('Owned reverse mapping remains installed')
        except Exception as error:
            cleanup_errors.append(str(error))
    if server is not None:
        try:
            server.terminate()
            try:
                server.wait(timeout=10)
            except subprocess.TimeoutExpired:
                server.kill()
                server.wait(timeout=10)
            if server.poll() is None:
                raise RuntimeError('Owned SMB fixture process remains active')
        except Exception as error:
            cleanup_errors.append(str(error))
    if server_log is not None:
        server_log.close()
    summary = {'passed': failure is None and not cleanup_errors, 'failure': failure, 'cleanupErrors': cleanup_errors,
               'smbTransport': 'adb reverse, synthetic fixture' if args.smb else 'not tested',
               'playerSettingsChanged': False, 'powerSettingsChanged': False}
    (report / 'result.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps(summary, ensure_ascii=False))
    print(report)
if not summary['passed']:
    sys.exit(1)
