"""PICO localization acceptance through the shared development UI input path.

Run against a development APK, preferably the isolated acceptance package.
Backups, labels and captures remain in ignored run folders. Original file bytes,
absence and USB stay-awake are verified on cleanup. Never changes screen timeout.
Physical-controller/readability acceptance requires --manual and a human response.
"""
import argparse
import datetime as dt
import hashlib
import json
from pathlib import Path
import re
import subprocess
import threading
import time
import uuid

ROOT = Path(__file__).resolve().parent.parent


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--serial', required=True)
    parser.add_argument('--adb', default='adb')
    parser.add_argument('--package', default='com.linxyi.LsVRPlayer.localizationtest')
    parser.add_argument('--manual', action='store_true')
    parser.add_argument('--manual-only', action='store_true', help='Run physical acceptance separately from the automated page suite')
    args = parser.parse_args()
    if args.manual_only:
        args.manual = True
    if not re.fullmatch(r'[A-Za-z0-9_.]+', args.package):
        raise ValueError('Invalid package')
    run = uuid.uuid4().hex
    folder = ROOT / 'Docs/Validation/LocalizationDevice' / run
    captures = ROOT / 'Docs/Captures/LocalizationDevice' / run
    folder.mkdir(parents=True)
    captures.mkdir(parents=True)
    external = '/sdcard/Android/data/' + args.package + '/files/'
    fixture = '/sdcard/Movies/VRPlayerTest/Localization-' + run
    stage = '/data/local/tmp/vrplayer-localization-' + run + '.json'
    video_name = '字幕_日本語.mp4'
    report = {'run': run, 'package': args.package, 'scope': 'manual-only' if args.manual_only else 'all', 'passed': False, 'checks': [], 'sections': [],
              'input': 'development pointer through VrUiInput; physical input recorded separately',
              'manual': {'requested': args.manual, 'passed': False}, 'restored': False}
    backup = {}
    usb_original = None
    storage_permission_original = None
    fixture_owned = False
    stage_owned = False
    health_stop = threading.Event()
    health_thread = None
    health_error = []
    wake_stop = threading.Event()
    wake_thread = None
    catalog = json.loads((ROOT / 'Assets/VRPlayer/Resources/Localization/Strings.json').read_text(encoding='utf8'))
    translations = {entry['zh']: entry for entry in catalog['entries']}
    names = {'Chinese': '简体中文', 'English': 'English', 'Japanese': '日本語'}

    def save():
        (folder / 'report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf8')

    def adb(*command, check=True):
        result = subprocess.run([args.adb, '-s', args.serial, *command], capture_output=True, timeout=45)
        if check and result.returncode:
            raise RuntimeError('ADB operation failed: ' + ' '.join(command[:4]))
        return result

    def read(path, private=False):
        command = ['shell', 'run-as', args.package, 'cat', path] if private else ['exec-out', 'cat', path]
        return adb(*command).stdout

    def exists(path, private=False):
        command = ['shell', 'run-as', args.package, 'test', '-e', path] if private else ['shell', 'test', '-e', path]
        result = adb(*command, check=False)
        if result.returncode not in (0, 1):
            raise RuntimeError('Could not verify file existence')
        return result.returncode == 0

    def preserve(path, private=False):
        present = exists(path, private)
        data = read(path, private) if present else None
        backup[(path, private)] = data
        if present:
            (folder / ('backup-' + hashlib.sha256(path.encode()).hexdigest()[:12])).write_bytes(data)

    def push_bytes(data, destination, private=False):
        nonlocal stage_owned
        local = folder / 'transfer.bin'
        local.write_bytes(data)
        if private:
            if not stage_owned:
                assert not exists(stage) and not exists(stage + '.owner'), 'Command stage already exists'
                owner_file = folder / 'stage.owner'
                owner_file.write_bytes(run.encode())
                adb('push', str(owner_file), stage + '.owner')
                stage_owned = True
            assert read(stage + '.owner') == run.encode(), 'Command stage ownership changed'
            adb('push', str(local), stage)
            adb('shell', 'run-as', args.package, 'cp', stage, destination)
        else:
            adb('push', str(local), destination)

    def health():
        if health_error:
            raise AssertionError(health_error[0])

    def status():
        return json.loads(read(external + 'playback-diagnostics.json'))

    def wait(read_value, predicate, timeout=35):
        until = time.monotonic() + timeout
        while time.monotonic() < until:
            health()
            try:
                value = read_value()
                if predicate(value):
                    return value
            except (ValueError, RuntimeError):
                pass
            time.sleep(.25)
        raise AssertionError('Timed out waiting for device state')

    def check_state(value):
        assert all(value.get(key) is True for key in ('hasFocus', 'presenceKnown', 'userPresent')), 'Headset focus or presence interrupted'
        assert value.get('timestampUtc') and isinstance(value.get('frame'), int), 'Missing fresh diagnostics'

    def monitor():
        stamp = None
        changed = time.monotonic()
        while not health_stop.wait(.5):
            try:
                value = status()
                check_state(value)
                new = (value['timestampUtc'], value['frame'])
                if new != stamp:
                    stamp = new
                    changed = time.monotonic()
                assert time.monotonic() - changed < 7, 'Diagnostics stopped updating'
                report['sections'][-1]['samples'] += 1
            except Exception:
                health_error.append('Continuous focus, presence or diagnostics failed; section invalid')
                return

    def start_health(name):
        nonlocal health_thread
        check_state(status())
        health_stop.clear()
        report['sections'].append({'name': name, 'samples': 0, 'passed': False})
        health_thread = threading.Thread(target=monitor, daemon=True)
        health_thread.start()

    def stop_health():
        nonlocal health_thread
        if health_thread is not None:
            health_stop.set()
            health_thread.join(timeout=3)
            report['sections'][-1]['passed'] = not health_error and report['sections'][-1]['samples'] > 0
            health_thread = None
            health()

    def command(action, **fields):
        health()
        payload = json.dumps({'action': action, **fields}, ensure_ascii=False).encode('utf8')
        push_bytes(payload, 'files/command.tmp', True)
        adb('shell', 'run-as', args.package, 'rm', '-f', 'files/command-result.json')
        adb('shell', 'run-as', args.package, 'mv', 'files/command.tmp', 'files/command.json')
        value = wait(lambda: json.loads(read('files/command-result.json', True)), lambda v: 'error' in v or (v.get('accepted') and v.get('action') == action))
        if 'error' in value:
            (folder / 'command-failure.json').write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf8')
            raise AssertionError('Device command failed: ' + action + ': ' + value['error'])
        check_state(value)
        return value

    def click(name):
        for pressed in (False, True, False):
            command('ui-pointer', name=name, flag=pressed, value=.5)

    def tr(key, language):
        return key if language == 'Chinese' else translations[key]['en' if language == 'English' else 'ja']

    def inspect(name, language, expected=()):
        value = command('localization-inspect')
        assert value['language'] == language, 'Wrong effective language'
        labels = value['labels']
        (folder / (name + '.json')).write_text(json.dumps(value, ensure_ascii=False, indent=2), encoding='utf8')
        texts = [entry['text'] for entry in labels]
        assert not any(entry['missingGlyphs'] for entry in labels), 'Missing glyphs in ' + name
        unexpected = [entry['text'] for entry in labels if entry['truncated']]
        assert not unexpected, 'Truncated labels in ' + name + ': ' + repr(unexpected)
        for text in expected:
            assert text in texts, 'Expected localized label absent: ' + text
        return value

    def capture(name):
        from PIL import Image, ImageStat
        target = captures / (name + '.png')
        for _ in range(8):
            health()
            result = adb('exec-out', 'screencap', '-p', check=False)
            if not result.returncode and result.stdout.startswith(b'\x89PNG'):
                target.write_bytes(result.stdout)
                try:
                    with Image.open(target) as picture:
                        picture.load()
                        assert max(ImageStat.Stat(picture.convert('RGB')).stddev) > 8, 'Black or invalid capture'
                    return
                except (OSError, AssertionError):
                    pass
            time.sleep(.25)
        raise AssertionError('No reviewable app capture: ' + name)

    def passed(name):
        report['checks'].append({'name': name, 'passed': True})
        save()
        print('PASS ' + name, flush=True)

    def launch():
        old = read(external + 'playback-diagnostics.json') if exists(external + 'playback-diagnostics.json') else None
        adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
        adb('shell', 'monkey', '-p', args.package, '-c', 'android.intent.category.LAUNCHER', '1')
        wait(lambda: read(external + 'playback-diagnostics.json'), lambda data: data != old, timeout=180)
        wait(status, lambda value: all(value.get(key) is True for key in ('hasFocus', 'presenceKnown', 'userPresent')))

    def select_language(language):
        command('menu', flag=True)
        value = command('tab', name='设置')
        current = value['languagePreference']
        click(tr('跟随系统', value['language']) if current == 'System' else names[current])
        inspect('choices-before-' + language, value['language'], names.values())
        click('Language ' + language)
        value = inspect(language + '-settings', language, (tr('界面语言', language),))
        assert value['languagePreference'] == language
        capture(language + '-settings')

    def restore():
        errors = []
        health_stop.set()
        if health_thread is not None:
            health_thread.join(timeout=3)
        wake_stop.set()
        if wake_thread is not None:
            wake_thread.join(timeout=3)
        adb('shell', 'am', 'force-stop', args.package, check=False)
        for (path, private), data in backup.items():
            try:
                if data is None:
                    prefix = ['shell', 'run-as', args.package] if private else ['shell']
                    adb(*prefix, 'rm', '-f', path)
                    assert not exists(path, private)
                else:
                    push_bytes(data, path, private)
                    assert read(path, private) == data
            except Exception:
                errors.append('File restoration failed: ' + path)
        try:
            if fixture_owned:
                assert read(fixture + '/.owner') == run.encode()
                for name in (video_name, '.owner'):
                    adb('shell', 'rm', '-f', fixture + '/' + name)
                adb('shell', 'rmdir', fixture)
                assert not exists(fixture)
            if stage_owned:
                assert read(stage + '.owner') == run.encode()
                adb('shell', 'rm', '-f', stage)
                adb('shell', 'rm', '-f', stage + '.owner')
                assert not exists(stage)
                assert not exists(stage + '.owner')
            if not fixture_owned and exists(fixture):
                raise AssertionError('Fixture ownership could not be established')
        except Exception:
            errors.append('Owned fixture or command cleanup failed')
        try:
            if storage_permission_original is not None:
                adb('shell', 'pm', 'grant' if storage_permission_original else 'revoke', args.package, 'android.permission.READ_EXTERNAL_STORAGE')
                restored = adb('shell', 'dumpsys', 'package', args.package).stdout.decode()
                assert ('android.permission.READ_EXTERNAL_STORAGE: granted=true' in restored) == storage_permission_original
                report['storagePermissionRestored'] = True
        except Exception:
            errors.append('Storage permission restoration failed')
        try:
            if usb_original is not None:
                if usb_original == 'null':
                    adb('shell', 'settings', 'delete', 'global', 'stay_on_while_plugged_in')
                else:
                    adb('shell', 'settings', 'put', 'global', 'stay_on_while_plugged_in', usb_original)
                assert adb('shell', 'settings', 'get', 'global', 'stay_on_while_plugged_in').stdout.decode().strip() == usb_original
        except Exception:
            errors.append('USB stay-awake restoration failed')
        report['restored'] = not errors
        if errors:
            report['cleanupErrors'] = errors
            report['passed'] = False
        save()
        if errors:
            raise RuntimeError('Acceptance cleanup or restoration failed')

    try:
        print('RUN ' + str(folder), flush=True)
        adb('shell', 'run-as', args.package, 'pwd')
        adb('shell', 'am', 'force-stop', args.package)
        assert not exists(stage) and not exists(stage + '.owner') and not exists(fixture), 'Run paths already exist'
        for filename in ('settings.json', 'privacy-consent.json', 'emby-accounts.json'):
            preserve(external + filename)
        for filename in ('files/command.json', 'files/command.tmp', 'files/command-result.json', 'shared_prefs/emby_accounts.xml', 'shared_prefs/emby_accounts.xml.bak'):
            preserve(filename, True)
        permissions = adb('shell', 'dumpsys', 'package', args.package).stdout.decode()
        assert 'android.permission.READ_EXTERNAL_STORAGE' in permissions
        storage_permission_original = 'android.permission.READ_EXTERNAL_STORAGE: granted=true' in permissions
        (folder / 'storage-permission.backup.json').write_text(json.dumps({'granted': storage_permission_original}), encoding='utf8')
        adb('shell', 'pm', 'grant', args.package, 'android.permission.READ_EXTERNAL_STORAGE')
        assert 'android.permission.READ_EXTERNAL_STORAGE: granted=true' in adb('shell', 'dumpsys', 'package', args.package).stdout.decode()
        usb_original = adb('shell', 'settings', 'get', 'global', 'stay_on_while_plugged_in').stdout.decode().strip()
        assert usb_original == 'null' or usb_original.isdigit(), 'Cannot back up USB stay-awake'
        (folder / 'usb-setting.backup.json').write_text(json.dumps({'value': usb_original}), encoding='utf8')
        temporary_usb = str((0 if usb_original == 'null' else int(usb_original)) | 2)
        adb('shell', 'settings', 'put', 'global', 'stay_on_while_plugged_in', temporary_usb)
        assert adb('shell', 'settings', 'get', 'global', 'stay_on_while_plugged_in').stdout.decode().strip() == temporary_usb
        def wake():
            while not wake_stop.wait(1):
                adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP', check=False)
        wake_thread = threading.Thread(target=wake, daemon=True)
        wake_thread.start()
        adb('shell', 'mkdir', fixture)
        push_bytes(run.encode(), fixture + '/.owner')
        fixture_owned = True
        adb('shell', 'cp', '/sdcard/Movies/VRPlayerTest/Recognition/Unmarked-A.mp4', fixture + '/' + video_name)
        settings = {'version': 3, 'language': 0, 'autoHide': False, 'loop': True, 'history': [], 'favorites': [], 'formatDetection': 1}
        push_bytes(json.dumps(settings).encode(), external + 'settings.json')
        print('Launching acceptance app; accept its privacy notice using the physical controller.', flush=True)
        launch()
        start_health('initial-system-language')
        first = inspect('System-initial', 'Chinese', (tr('界面语言', 'Chinese'),)) if command('tab', name='设置') else None
        assert first['languagePreference'] == 'System'
        passed('System default resolves the headset Chinese locale')
        stop_health()
        for language in (() if args.manual_only else names):
            start_health(language + '-UI')
            select_language(language)
            stored = json.loads(read(external + 'settings.json'))
            assert stored['language'] == {'Chinese': 1, 'English': 2, 'Japanese': 3}[language]
            stop_health()
            adb('shell', 'am', 'force-stop', args.package)
            launch()
            start_health(language + '-restart-and-pages')
            assert command('inspect')['languagePreference'] == language
            command('open', uri='file://' + fixture + '/' + video_name, name=video_name)
            wait(status, lambda value: value['snapshot']['state'] == 3 and value['snapshot']['playing'])
            command('pause')
            command('menu', flag=True)
            command('browse', uri='file://' + fixture + '/')
            wait(status, lambda value: not value['loading'] and value['directory'] == 'file://' + fixture + '/')
            inspect(language + '-files', language, (video_name, tr('名称', language)))
            capture(language + '-files')
            for tab, heading in (('投影', '投影与立体布局'), ('字幕', '同目录匹配字幕'), ('信息', '播放信息与诊断')):
                command('menu', flag=True)
                command('tab', name=tab)
                inspect(language + '-' + tab, language, (tr(heading, language),))
                capture(language + '-' + tab)
                if tab == '字幕':
                    click('样式')
                    inspect(language + '-subtitle-style', language, (tr('预设样式', language),))
                    capture(language + '-subtitle-style')
                    click('选择')
            command('tab', name='文件')
            command('source', name='emby')
            inspect(language + '-emby-accounts', language, (tr('Emby · 服务器与账户', language),))
            click(tr('添加服务器 / 账户', language))
            inspect(language + '-emby-login', language, (tr('连接协议', language), tr('登录并打开媒体库', language)))
            capture(language + '-emby-login')
            click('返回')
            command('tab', name='文件')
            command('source', name='smb')
            command('stop-scan')
            inspect(language + '-smb', language, (tr('已保存的共享', language), tr('局域网发现', language)))
            capture(language + '-smb')
            for key, document in (('隐私政策', 'privacy'), ('权限说明', 'permissions'), ('开源许可', 'licenses')):
                command('tab', name='信息')
                click(tr(key, language))
                value = inspect(language + '-' + document, language)
                texts = [entry['text'] for entry in value['labels']]
                if document != 'licenses':
                    expected = "L's VRPlayer 隐私政策" if language == 'Chinese' else "L's VRPlayer Privacy Policy"
                    if document == 'privacy':
                        assert any(text.startswith(expected) for text in texts)
                else:
                    heading = '运行时组件 / Runtime components' if language == 'Chinese' else 'Runtime components / 运行时组件'
                    assert heading in texts
                    assert any('androidx.media3:media3-exoplayer:' in text for text in texts)
                    assert any('Apache License' in text for text in texts)
                capture(language + '-' + document)
                click('返回')
            command('quick-menu', flag=True)
            for popup, heading in (('mode', '模式切换'), ('subtitles', '字幕进度调节'), ('advanced', '倍速')):
                command('quick-popup', name=popup)
                inspect(language + '-quick-' + popup, language, (tr(heading, language),))
                capture(language + '-quick-' + popup)
            passed(language + ': UI selection, persisted restart, original media name, playback, legal pages and quick panels')
            stop_health()
        start_health('system-follow-and-manual')
        select_language('Chinese')
        click(names['Chinese'])
        click('Language System')
        value = inspect('System-restored', 'Chinese', (tr('跟随系统', 'Chinese'),))
        assert value['languagePreference'] == 'System'
        passed('Explicit selection can return to following the real headset locale')
        if args.manual:
            select_language('Japanese')
            command('ui-input-reset')
            command('ui-input-stats-reset')
            (folder / 'manual-ready.json').write_text(json.dumps({'run': run, 'instructions': 'Use physical controls to switch Japanese to English; confirm readable English/Japanese and Back.'}), encoding='utf8')
            print('MANUAL READY: Japanese settings; physical input enabled. Awaiting human verification.', flush=True)
            answer_path = folder / 'manual-answer.json'
            wait(lambda: answer_path.exists(), bool, timeout=600)
            answer = json.loads(answer_path.read_text(encoding='utf8'))
            state = command('inspect')
            (folder / 'manual-observed-state.json').write_text(json.dumps(state, ensure_ascii=False, indent=2), encoding='utf8')
            assert not state['input']['diagnostic'], 'Physical input is still overridden'
            assert state['languagePreference'] == 'English', 'Physical language switch not observed: ' + state['languagePreference']
            assert state['input']['leftSamples'] + state['input']['rightSamples'] > 0, 'No physical controller tracking'
            assert answer.get('passed') is True, 'Human acceptance did not pass'
            report['manual'] = {'requested': True, 'passed': True, 'human': answer, 'observedPreference': state['languagePreference'], 'input': state['input']}
            passed('Human confirms worn readability and physical input; English choice observed with simulation disabled')
        command('ui-input-reset')
        command('stop')
        stop_health()
        report['passed'] = True
    except Exception as error:
        report['error'] = str(error)
        save()
        raise
    finally:
        restore()
        print('Restoration verified: ' + str(report['restored']), flush=True)
        print('REPORT ' + str(folder / 'report.json'), flush=True)


if __name__ == '__main__':
    main()
