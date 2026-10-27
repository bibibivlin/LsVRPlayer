"""Test thumbnail UI in an isolated development build on a worn, focused PICO.

Build VRPlayer.Editor.DeviceValidationBuild.BuildThumbnails first. No production
player files or power settings are changed. Synthetic clips only; controller
events use the shared development pointer driver, not physical buttons.
"""
import argparse
from datetime import datetime, timezone
import hashlib
import importlib.util
import json
from pathlib import Path
import sys
import threading
import time

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'Tools/.runtime'))
spec = importlib.util.spec_from_file_location('back_navigation', ROOT / 'Tools/Device-BackNavigationTest.py')
base = importlib.util.module_from_spec(spec)
spec.loader.exec_module(base)
base.PACKAGE = PACKAGE = 'com.linxyi.LsVRPlayer.thumbnailtest'
base.EXTERNAL = EXTERNAL = '/sdcard/Android/data/' + PACKAGE + '/files/'


class ThumbnailUiRun(base.BackNavigationRun):
    def __init__(self, args):
        super().__init__(args)
        # Keep the inherited unique staging path and marker ownership checks.
        self.output.rmdir()
        self.output = ROOT / 'Docs/Validation/SeekThumbnails/FullApp' / ('app-' + self.token)
        self.output.mkdir(parents=True)
        self.install_attempted = False
        self.report.update(input='development pointer events through production VrUiInput and uGUI',
                           limitations=['Physical trigger bindings and comfort require manual acceptance.',
                                        'Generated local clips only; high-resolution checks are short, not long-film acceptance.'],
                           productionPlayerChanged=False)

    def prepare(self):
        if self.adb('shell', 'pm', 'list', 'packages', PACKAGE).stdout.strip():
            raise RuntimeError('Isolated test package already exists; do not overwrite unknown test data')
        apk = ROOT / 'Builds/ThumbnailValidation.apk'
        if not apk.exists():
            raise FileNotFoundError('Build the isolated thumbnail validation APK first')
        from PIL import Image
        self.report['apkSha256'] = hashlib.sha256(apk.read_bytes()).hexdigest()
        self.report['thumbnailUiSourceSha256'] = hashlib.sha256((ROOT / 'Assets/VRPlayer/Runtime/UI/SeekThumbnailPreview.cs').read_bytes()).hexdigest()
        self.report['thumbnailGraphicSourceSha256'] = hashlib.sha256((ROOT / 'Assets/VRPlayer/Runtime/UI/RoundedRawImage.cs').read_bytes()).hexdigest()
        self.report['testScriptSha256'] = hashlib.sha256(Path(__file__).read_bytes()).hexdigest()
        self.write_report()
        self.install_attempted = True
        assert b'Success' in self.adb('install', str(apk)).stdout
        self.adb('shell', 'mkdir', '-p', EXTERNAL + 'fixtures')
        self.adb('shell', 'mkdir', '-p', self.remote_temp)
        marker = self.output / '.owner'
        marker.write_text(self.token, encoding='ascii')
        self.adb('push', str(marker), self.remote_temp + '/.owner')
        self.owned_temp = True
        files = {'timeline.mp4': ROOT / 'TestMedia/Thumbnails/seek-preview.mp4',
                 'replacement.mp4': ROOT / 'TestMedia/Thumbnails/seek-preview.mp4',
                 'sbs.mp4': ROOT / 'TestMedia/Stereo.180.SBS.mp4',
                 'ou.mp4': ROOT / 'TestMedia/Stereo.360.OU.mp4',
                 '4k.mp4': ROOT / 'TestMedia/Performance.4K60.HEVC.mp4',
                 '8k.mp4': ROOT / 'TestMedia/Performance.8K30.HEVC.mp4'}
        for name, path in files.items():
            self.adb('push', str(path), EXTERNAL + 'fixtures/' + name)
        settings = self.output / 'fixture-settings.json'
        settings.write_text(json.dumps(dict(version=3, language=1, history=[], favorites=[], volume=0, speed=1,
                                           loop=True, autoHide=False, autoLoadSubtitles=False, formatDetection=0)), encoding='utf-8')
        self.adb('push', str(settings), EXTERNAL + 'settings.json')
        # The isolated fixture uses the same policy already accepted in the installed app.
        consent = self.adb('exec-out', 'cat', '/sdcard/Android/data/com.linxyi.LsVRPlayer/files/privacy-consent.json').stdout
        assert json.loads(consent)['version'] == '2026-10-17.1', 'Current privacy policy has not been accepted'
        consent_file = self.output / 'consent.backup'
        consent_file.write_bytes(consent)
        self.adb('push', str(consent_file), EXTERNAL + 'privacy-consent.json')
        self.adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
        self.adb('shell', 'monkey', '-p', PACKAGE, '-c', 'android.intent.category.LAUNCHER', '1')
        initial = self.wait(self.read_status, lambda s: self.valid_presence(s) and s.get('timestampUtc') and s.get('frame', 0) > 0,
                            'fresh startup with headset presence and focus', timeout=60)
        self.diag_stamp, self.diag_frame = initial['timestampUtc'], initial['frame']
        self.diag_changed_at = time.monotonic()
        self.monitor_thread = threading.Thread(target=self.monitor, daemon=True)
        self.monitor_thread.start()
        self.command('ui-input-reset')

    def open_clip(self, name):
        self.pointer()
        self.command('open', uri='file://' + EXTERNAL + 'fixtures/' + name, name='Generated ' + name)
        self.wait(self.read_status, lambda s: s['snapshot']['state'] == 3 and s['snapshot']['duration'] > 0, 'clip ready')
        self.command('pause')
        self.command('seek', position=1000)
        self.wait(self.read_status, lambda s: not s['snapshot']['playing'] and abs(s['snapshot']['position'] - 1000) < 250, 'paused baseline')
        self.command('projection', projection=0, stereo=0)

    def read_bytes(self, path, private=False):
        data = super().read_bytes(path, private)
        if private and path == 'files/command-result.json':
            try:
                response = json.loads(data)
            except json.JSONDecodeError:
                return data
            if response.get('error'):
                (self.output / 'command-failure.json').write_bytes(data)
                raise AssertionError('Device diagnostic command failed; see ignored command-failure.json')
        return data

    def snapshot(self):
        return self.read_status()['snapshot']

    def thumbnail(self, label='thumbnail loaded'):
        return self.wait_ui(lambda s: s.get('thumbnail', {}).get('visible') and s['thumbnail']['hasImage'], label)['thumbnail']

    def capture(self, name):
        self.guard()
        # Capture the headset compositor; Unity ScreenCapture is not reliable in this XR session.
        from PIL import Image
        destination = self.output / (name + '.png')
        self.adb('shell', 'screencap', '-p', self.remote_temp + '/capture.png')
        self.adb('pull', self.remote_temp + '/capture.png', str(destination))
        with Image.open(destination) as image:
            image.verify()
        self.guard()

    def run_checks(self):
        self.open_clip('timeline.mp4')
        self.command('quick-menu', flag=True)
        self.pointer('Seek surface', value=.8)
        thumb = self.thumbnail()
        assert thumb['key'] == 8000 and thumb['width'] <= 320 and thumb['height'] <= 180
        assert abs(self.snapshot()['position'] - 1000) < 250
        self.capture('01-quick-blue-preview')
        self.passed('Quick hover previews blue segment while main playback remains paused at red segment', thumbnail=thumb)
        self.pointer('Seek surface', value=.45)
        thumb = self.thumbnail(); assert thumb['key'] == 4000
        self.capture('02-quick-green-preview')
        self.passed('Moving hover replaces the preview with the corresponding green segment', thumbnail=thumb)

        self.pointer('Seek surface', True, .45)
        self.command('ui-pointer', preset=1, name='Seek surface', value=.15, flag=True)
        self.command('ui-pointer', preset=1, name='Seek surface', value=.15, flag=False)
        assert abs(self.snapshot()['position'] - 1000) < 250
        self.pointer('Seek surface', True, .8); self.thumbnail()
        assert abs(self.snapshot()['position'] - 1000) < 250
        self.pointer('Seek surface', False, .8)
        self.wait(self.read_status, lambda s: abs(s['snapshot']['position'] - 9600) < 350, 'seek committed on owner release')
        self.passed('Other controller cannot commit owned drag; owning hand release commits target')
        self.command('ui-pointer', preset=1, name='', flag=False)

        self.pointer('Seek surface', True, .25)
        self.command('menu', flag=True); self.pointer()
        self.sleep(2.2); assert abs(self.snapshot()['position'] - 9600) < 350
        self.passed('Switching panels cancels drag without committing a seek')
        self.command('seek', position=1000)
        self.wait(self.read_status, lambda s: abs(s['snapshot']['position'] - 1000) < 250, 'distinct full timeline baseline')
        self.pointer('播放进度', value=.45); thumb = self.thumbnail()
        assert thumb['key'] == 4000
        assert abs(self.snapshot()['position'] - 1000) < 250
        self.capture('03-full-green-preview')
        self.passed('Full timeline loads thumbnail through actual world-space input', thumbnail=thumb)
        self.pointer('播放进度', True, .2); self.pointer('播放进度', True, .8)
        self.thumbnail(); self.sleep(2.2)
        assert abs(self.snapshot()['position'] - 1000) < 250
        self.pointer('播放进度', False, .8)
        self.wait(self.read_status, lambda s: 9000 < s['snapshot']['position'] < 10500, 'full timeline release seek')
        self.passed('Full timeline drag commits only when released')

        self.open_clip('replacement.mp4'); self.command('quick-menu', flag=True)
        self.pointer('Seek surface', value=.45); thumb = self.thumbnail()
        generation = self.snapshot()['generation']; assert thumb['generation'] == generation
        self.passed('Replacement film cannot display previous generation thumbnail', thumbnail=thumb)
        self.command('stop')
        self.wait_ui(lambda s: not s.get('thumbnail') or not s['thumbnail']['visible'], 'stop hides preview')
        self.passed('Stopping playback dismisses the preview')

        for clip, stereo, expected in [('sbs.mp4', 1, 'x'), ('ou.mp4', 2, 'y')]:
            self.open_clip(clip); self.command('projection', projection=0, stereo=stereo)
            self.command('quick-menu', flag=True); self.pointer('Seek surface', value=.5)
            first = self.thumbnail()
            self.command('eyes', flag=True); second = self.thumbnail()
            assert abs(first['eye'][expected] - second['eye'][expected]) == .5
            self.capture('04-' + clip + '-swapped-eye')
            self.passed('Single-eye preview honors eye swap: ' + clip, before=first['eye'], after=second['eye'])

        for clip in ['timeline.mp4', '4k.mp4', '8k.mp4']:
            self.open_clip(clip); self.command('quick-menu', flag=True)
            self.command('play'); before = self.wait(self.read_status, lambda s: s['snapshot']['playing'], 'playing')['snapshot']
            self.pointer('Seek surface', value=.65)
            thumb = self.thumbnail('playing ' + clip + ' thumbnail')
            self.sleep(2.2); after = self.snapshot()
            assert after['playing'] and not after.get('error') and after['generation'] == before['generation']
            advanced = (after['position'] - before['position']) % after['duration']
            assert advanced >= 1500, 'Playback did not advance while the thumbnail loaded'
            self.capture('05-playing-' + clip)
            self.passed('Preview during uninterrupted short-clip playback: ' + clip, width=after['width'], height=after['height'],
                        positionBefore=before['position'], positionAfter=after['position'], advancedMs=advanced,
                        droppedBefore=before.get('dropped'), droppedAfter=after.get('dropped'), thumbnail=thumb)
        self.command('ui-input-reset'); self.guard()

    def restore(self):
        self.monitor_stop.set()
        if self.monitor_thread:
            self.monitor_thread.join(timeout=5)
        errors = []
        if self.monitor_error:
            self.report['passed'] = False
            self.report['continuityError'] = self.monitor_error
        if self.install_attempted:
            try:
                if self.adb('shell', 'pm', 'list', 'packages', PACKAGE).stdout.strip():
                    self.adb('shell', 'am', 'force-stop', PACKAGE)
                    assert b'Success' in self.adb('uninstall', PACKAGE).stdout
                assert not self.adb('shell', 'pm', 'list', 'packages', PACKAGE).stdout.strip()
            except Exception:
                errors.append('Isolated test package cleanup failed')
        if self.owned_temp:
            try:
                assert self.remote_temp == '/data/local/tmp/vrplayer-back-' + self.token
                assert self.read_bytes(self.remote_temp + '/.owner').decode('ascii') == self.token
                self.adb('shell', 'rm', '-rf', self.remote_temp)
                assert not self.exists(self.remote_temp)
            except Exception:
                errors.append('Owned command staging cleanup failed')
        self.report['cleanupPassed'] = not errors
        self.report['cleanupErrors'] = errors
        self.report['passed'] = self.report.get('passed', False) and not errors and not self.monitor_error
        self.write_report()
        if errors:
            raise RuntimeError('Cleanup failed; see report')

    def execute(self):
        try:
            self.prepare(); self.run_checks(); self.report['passed'] = True
        except Exception as error:
            self.report['error'] = str(error)
            raise
        finally:
            self.restore()
            print('Report: ' + str(self.output / 'report.json'), flush=True)


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--serial', required=True)
    parser.add_argument('--adb', default=base.ADB_DEFAULT)
    ThumbnailUiRun(parser.parse_args()).execute()
