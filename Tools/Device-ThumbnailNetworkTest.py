"""Read real Emby originals in the isolated native test package; no player stores are changed."""
import argparse
import copy
import getpass
import hashlib
import importlib.util
import json
import os
from pathlib import Path
import subprocess
import urllib.parse
import uuid

ROOT = Path(__file__).resolve().parent.parent
spec = importlib.util.spec_from_file_location('emby_validation', ROOT / 'Tools/Device-EmbyTest.py')
base = importlib.util.module_from_spec(spec)
spec.loader.exec_module(base)
PACKAGE = 'com.linxyi.vrplayer.test'
EXTERNAL = '/sdcard/Android/data/' + PACKAGE + '/files/'


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--serial', required=True)
    parser.add_argument('--server', required=True)
    parser.add_argument('--username', required=True)
    parser.add_argument('--count', type=int, default=3)
    parser.add_argument('--min-width', type=int, default=3840)
    parser.add_argument('--concurrent-playback', action='store_true')
    parser.add_argument('--adb', default=base.DEFAULT_ADB)
    args = parser.parse_args()
    if not 1 <= args.count <= 6:
        parser.error('--count must be 1..6')
    output = ROOT / 'Docs/Validation/SeekThumbnails' / ('network-' + uuid.uuid4().hex[:12])
    output.mkdir(parents=True)
    report = {'passed': False, 'transport': 'real LAN Emby original HTTP ranges', 'playerStoresChanged': False,
              'powerSettingsChanged': False, 'concurrentPlayback': args.concurrent_playback,
              'limitations': ['Native decoder regression; no Unity UI, XR presence, physical controllers or worn-headset acceptance.']}
    server = base.Server(args.server)
    originals = {}
    installed = False
    config_file = output / 'session.private.json'
    cleanup_errors = []

    def adb(*parts, timeout=60):
        result = subprocess.run([args.adb, '-s', args.serial, *parts], capture_output=True, timeout=timeout)
        if result.returncode:
            raise RuntimeError('ADB operation failed (private device output omitted)')
        return result.stdout

    try:
        if adb('shell', 'pm', 'list', 'packages', PACKAGE).strip():
            raise RuntimeError('The isolated test package already exists; refusing to overwrite it')
        password = os.environ.get('EMBY_TEST_PASSWORD')
        if password is None:
            password = getpass.getpass('Emby test password: ')
        server.login(args.username, password)
        password = ''
        # Inspect only the user-designated account. Do not print names, IDs, paths or URLs.
        query = {'Recursive': 'true', 'MediaTypes': 'Video', 'Fields': 'MediaSources,MediaStreams', 'Limit': 200}
        items = []
        for start in range(0, 2000, 200):
            page = server.request(server.user_path('/Items?') + urllib.parse.urlencode({**query, 'StartIndex': start}))
            items.extend(page.get('Items', []))
            if start + 200 >= page.get('TotalRecordCount', 0):
                break
        candidates = []
        for item in items:
            source = next(iter(item.get('MediaSources', [])), {})
            stream = next((s for s in source.get('MediaStreams', item.get('MediaStreams', [])) if s.get('Type') == 'Video'), {})
            if stream.get('Width', 0) >= args.min_width and item.get('RunTimeTicks', 0) >= 90 * 10_000_000:
                candidates.append((source.get('Bitrate', stream.get('BitRate', 0)) or 0, item, source, stream))
        candidates.sort(key=lambda value: value[0], reverse=True)
        if not candidates:
            report['error'] = 'No accessible original of at least 90 seconds with width >= ' + str(args.min_width)
            raise RuntimeError('No accessible 4K/8K originals of at least 90 seconds in the designated account')
        cases, inventory = [], []
        for index, (bitrate, item, source, stream) in enumerate(candidates[:args.count]):
            originals[item['Id']] = copy.deepcopy(server.item(item['Id']).get('UserData', {}))
            duration = item['RunTimeTicks'] // 10000
            url = server.address + '/Videos/' + urllib.parse.quote(item['Id'], safe='') + '/stream?' + urllib.parse.urlencode({'Static': 'true', 'MediaSourceId': source['Id']})
            cases.append({'url': url, 'positions': [int(duration * fraction) for fraction in (.2, .5, .8)]})
            inventory.append({'case': index, 'width': stream.get('Width'), 'height': stream.get('Height'),
                              'codec': stream.get('Codec'), 'container':source.get('Container'), 'bitDepth':stream.get('BitDepth'),
                              'bitrate': bitrate, 'durationMs': duration})
        (output / 'userdata.backup.json').write_text(json.dumps(originals), encoding='utf-8')
        report['samples'] = inventory
        apk = ROOT / 'Native/Android/build/outputs/apk/androidTest/debug/vrplayer-android-debug-androidTest.apk'
        report['apkSha256'] = hashlib.sha256(apk.read_bytes()).hexdigest()
        config_file.write_text(json.dumps({'token': server.token, 'cases': cases, 'concurrentPlayback': args.concurrent_playback}), encoding='utf-8')
        installed = True
        if b'Success' not in adb('install', str(apk), timeout=120):
            raise RuntimeError('Isolated installation was not confirmed')
        adb('shell', 'mkdir', '-p', EXTERNAL)
        adb('push', str(config_file), EXTERNAL + 'thumbnail-real.json')
        result = adb('shell', 'am', 'instrument', '-w', '-r', '-e', 'class',
                     'com.linxyi.vrplayer.SeekThumbnailRealNetworkTest',
                     PACKAGE + '/androidx.test.runner.AndroidJUnitRunner', timeout=700)
        (output / 'instrumentation.txt').write_bytes(result)
        metrics = json.loads(adb('exec-out', 'cat', EXTERNAL + 'thumbnail-real-result.json'))
        report['metrics'] = metrics
        report['passed'] = len(metrics) == len(cases) * 3 and all(m['ok'] for m in metrics) and b'OK (' in result
    except Exception:
        report.setdefault('error', 'Real-network regression failed; private details were not printed')
    finally:
        if installed:
            try:
                adb('shell', 'am', 'force-stop', PACKAGE)
                if b'Success' not in adb('uninstall', PACKAGE):
                    raise RuntimeError()
                if adb('shell', 'pm', 'list', 'packages', PACKAGE).strip():
                    raise RuntimeError()
            except Exception:
                cleanup_errors.append('Owned test package cleanup failed')
        for item_id, original in originals.items():
            try:
                current = server.item(item_id).get('UserData', {})
                if current != original:
                    server.restore_user_data(item_id, original)
                if server.item(item_id).get('UserData', {}) != original:
                    raise RuntimeError()
            except Exception:
                cleanup_errors.append('Server UserData verification failed')
        if server.token:
            try:
                server.request('/Sessions/Logout', 'POST', {})
            except Exception:
                cleanup_errors.append('Temporary server session logout failed')
        config_file.unlink(missing_ok=True)
        report['cleanupErrors'] = cleanup_errors
        report['passed'] = report['passed'] and not cleanup_errors
        (output / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        print(json.dumps(report), flush=True)
        print('Report: ' + str(output / 'report.json'), flush=True)
    return 0 if report['passed'] else 1


if __name__ == '__main__':
    raise SystemExit(main())
