"""Verify rendering presets and LAN discovery on one explicitly selected development device."""
import argparse
import json
from pathlib import Path
import subprocess
import time

ROOT = Path(__file__).resolve().parent.parent
PACKAGE = 'com.linxyi.LsVRPlayer'
EXTERNAL = '/sdcard/Android/data/' + PACKAGE + '/files/'
parser = argparse.ArgumentParser()
parser.add_argument('--serial', required=True)
parser.add_argument('--host', default='')
parser.add_argument('--port', type=int, default=1445)
parser.add_argument('--username', default='')
parser.add_argument('--password', default='')
parser.add_argument('--adb', default=r'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe')
args = parser.parse_args()
report = {'device': args.serial, 'started': time.strftime('%Y-%m-%d %H:%M:%S'), 'checks': []}
report_path = ROOT / ('Docs/Validation/experience-device-auth.json' if args.username else 'Docs/Validation/experience-device.json')
report_path.parent.mkdir(parents=True, exist_ok=True)

def adb(*command, check=True):
    return subprocess.run([args.adb, '-s', args.serial, *command], capture_output=True, check=check).stdout.decode('utf-8', errors='replace')

def wait_for(read, predicate, timeout=35):
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        try:
            last = read()
            if predicate(last):
                return last
        except (json.JSONDecodeError, subprocess.CalledProcessError):
            pass
        time.sleep(.4)
    raise AssertionError('Timeout: ' + str(last))

def command(action, **fields):
    adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
    payload = ROOT / 'TestMedia/experience-command.json'
    payload.write_text(json.dumps({'action': action, **fields}, ensure_ascii=False), encoding='utf-8')
    adb('shell', 'run-as', PACKAGE, 'rm', '-f', 'files/command-result.json')
    adb('push', str(payload), '/data/local/tmp/vrplayer-experience.json')
    adb('shell', 'run-as', PACKAGE, 'cp', '/data/local/tmp/vrplayer-experience.json', 'files/command.tmp')
    adb('shell', 'run-as', PACKAGE, 'mv', 'files/command.tmp', 'files/command.json')
    return wait_for(lambda: json.loads(adb('shell', 'run-as', PACKAGE, 'cat', 'files/command-result.json')), lambda x: x.get('accepted') and x.get('action') == action)

def status():
    adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
    return json.loads(adb('shell', 'cat', EXTERNAL + 'playback-diagnostics.json'))

def record(name, evidence):
    report['checks'].append({'name': name, 'passed': True, 'evidence': evidence})
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    print('PASS ' + name, flush=True)

def enumerate_shares(username='', password=''):
    adb('shell', 'run-as', PACKAGE, 'rm', '-f', 'files/smb-shares-result.json')
    command('shares', host=args.host, port=args.port, username=username, password=password)
    return wait_for(lambda: json.loads(adb('shell', 'run-as', PACKAGE, 'cat', 'files/smb-shares-result.json')), lambda s: 'ok' in s)

try:
    for index, scale in enumerate([1, 1.25, 1.5]):
        command('quality', preset=index)
        state = wait_for(status, lambda s: abs(s.get('renderScale', 0) - scale) < .02 and s.get('msaa') == 4)
        time.sleep(3)
        state = status()
        record('Render scale ' + str(scale), {key: state[key] for key in ['eyeWidth', 'eyeHeight', 'renderScale', 'msaa', 'unityFps']})
    command('quality', preset=1)
    for tab in ['文件', '投影', '字幕', '设置', '信息']:
        result = command('tab', name=tab)
        assert result['tab'] == tab
    record('Five menu tabs', True)
    command('tab', name='文件')
    command('scan')
    scan = wait_for(lambda: command('inspect')['discovery'], lambda s: not s['running'])
    record('LAN discovery completed', scan)
    if args.host:
        assert any(h['host'] == args.host and h['port'] == args.port for h in scan['hosts']), scan
        record('mDNS fixture discovered and SMB negotiated', True)
        if args.username:
            assert not enumerate_shares()['ok']
            record('Authenticated fixture rejects anonymous access', True)
            assert not enumerate_shares(args.username, 'intentionally-wrong-fixture-password')['ok']
            record('Incorrect password rejected', True)
        shares = enumerate_shares(args.username, args.password)
        assert shares['ok'] and any(s['name'].casefold() == 'testmedia' for s in shares['shares']), shares
        record('SRVSVC lists fixture share', shares)
    command('scan')
    command('stop-scan')
    stopped = command('inspect')['discovery']
    assert not stopped['running']
    command('tab', name='设置')
    time.sleep(2)
    assert command('inspect')['tab'] == '设置'
    record('Cancel discovery and switch page', True)
    command('quality', preset=1)
    command('tab', name='文件')
except Exception as error:
    report['error'] = str(error)
    report_path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    raise
