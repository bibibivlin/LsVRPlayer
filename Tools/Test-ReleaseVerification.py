"""Regression tests for release publication gates; no Android SDK required."""
import importlib.util
from pathlib import Path
import tempfile
import unittest
import json
import zipfile

spec = importlib.util.spec_from_file_location("release_verifier", Path(__file__).with_name("Verify-Release.py"))
release = importlib.util.module_from_spec(spec)
spec.loader.exec_module(release)
MANIFEST = '''<manifest xmlns:android="http://schemas.android.com/apk/res/android"
 package="com.linxyi.LsVRPlayer" android:versionName="1.0.0" android:versionCode="7">
 <uses-sdk android:minSdkVersion="29" android:targetSdkVersion="35"/>
 <application android:debuggable="false" android:allowBackup="false" android:fullBackupContent="false"/></manifest>'''
MANIFEST = MANIFEST.replace(' <application', ''.join('<uses-permission android:name="'+name+'"'+(' android:maxSdkVersion="32"' if name.endswith('READ_EXTERNAL_STORAGE') else '')+'/>' for name in sorted(release.legal.PERMISSIONS))+' <application')
FINGERPRINT = "a" * 64


class ReleaseVerificationTests(unittest.TestCase):
    def test_release_manifest_accepts_explicit_false_and_absent_debug_flag(self):
        release.verify_manifest(MANIFEST, "1.0.0", 7)
        release.verify_manifest(MANIFEST.replace(' android:debuggable="false"', ""), "1.0.0", 7)

    def test_debuggable_and_test_only_packages_cannot_be_published(self):
        for attribute in ('android:debuggable="true"', 'android:debuggable="1"', 'android:testOnly="true"'):
            with self.subTest(attribute=attribute), self.assertRaises(ValueError):
                release.verify_manifest(MANIFEST.replace('android:debuggable="false"', attribute), "1.0.0", 7)

    def test_wrong_package_version_code_and_sdk_are_rejected(self):
        for old, new in (("com.linxyi.LsVRPlayer", "com.example.other"), ('versionName="1.0.0"', 'versionName="0.6.0"'),
                         ('versionCode="7"', 'versionCode="6"'), ('targetSdkVersion="35"', 'targetSdkVersion="34"')):
            with self.subTest(change=new), self.assertRaises(ValueError):
                release.verify_manifest(MANIFEST.replace(old, new), "1.0.0", 7)

    def test_only_the_expected_single_signer_is_accepted(self):
        text = "Signer #1 certificate SHA-256 digest: " + FINGERPRINT + "\n"
        self.assertEqual(FINGERPRINT, release.verify_signer(text, FINGERPRINT.upper()))
        for invalid in ("", text.replace("a", "b"), text + text.replace("#1", "#2")):
            with self.subTest(signatures=invalid), self.assertRaises(ValueError):
                release.verify_signer(invalid, FINGERPRINT)

    def test_unexplained_permissions_unbounded_storage_and_backup_block_release(self):
        for text in (MANIFEST.replace('<application','<uses-permission android:name="android.permission.CAMERA"/><application'),
                     MANIFEST.replace(' android:maxSdkVersion="32"',''),
                     MANIFEST.replace('android:allowBackup="false"','android:allowBackup="true"'),
                     MANIFEST.replace(' android:fullBackupContent="false"',''),
                     MANIFEST.replace('android.permission.INTERNET','android.permission.RECORD_AUDIO')):
            with self.subTest(manifest=text),self.assertRaises(ValueError):
                release.verify_manifest(text,'1.0.0',7)

    def test_missing_failed_or_inconclusive_unity_tests_block_publication(self):
        with tempfile.TemporaryDirectory() as temp:
            report = Path(temp) / "results.xml"
            for result in ('result="Failed" passed="2" failed="1"', 'result="Passed" passed="0"',
                           'result="Passed" passed="2" inconclusive="1"'):
                report.write_text("<test-run " + result + "/>")
                with self.subTest(result=result), self.assertRaises(ValueError):
                    release.test_summary(report)
            report.write_text('<test-run result="Passed" passed="3" failed="0" skipped="0"/>')
            self.assertEqual(3, release.test_summary(report)["passed"])


class LegalApkIntegrityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.root=Path(__file__).resolve().parent.parent
        legal=cls.root/'Assets/StreamingAssets/Legal'
        manifest=json.loads((cls.root/'Native/FFmpeg/binary-manifest.json').read_text())
        cls.members={'assets/Legal/'+path.name:path.read_bytes() for path in legal.iterdir() if path.suffix in ('.txt','.json','.zip')}
        native=cls.root/'Assets/Plugins/Android/vrplayer-ffmpeg-lgpl.aar'
        with zipfile.ZipFile(native) as aar:
            cls.members.update({'lib/arm64-v8a/'+name:aar.read('jni/arm64-v8a/'+name) for name in manifest['libraries']})
        cls.members['lib/arm64-v8a/libvrplayerass.so']=(cls.root/'Assets/Plugins/Android/arm64-v8a/libvrplayerass.so').read_bytes()

    def test_matching_apk_materials_and_libraries_are_accepted(self):
        with tempfile.TemporaryDirectory() as folder:
            apk=Path(folder)/'fixture.apk'
            with zipfile.ZipFile(apk,'w') as archive:
                for name,data in self.members.items():archive.writestr(name,data)
            self.assertEqual(37,release.legal.verify_apk(apk,self.root)['runtimeComponents'])

    def test_replaced_library_source_or_policy_blocks_distribution(self):
        for member in ('lib/arm64-v8a/libavcodec.so','assets/Legal/ffmpeg-lgpl-corresponding-source.zip','assets/Legal/privacy-en.txt',
                       'lib/arm64-v8a/libvrplayerass.so','assets/Legal/ass-corresponding-source.zip'):
            with self.subTest(member=member),tempfile.TemporaryDirectory() as folder:
                apk=Path(folder)/'tampered.apk'
                with zipfile.ZipFile(apk,'w') as archive:
                    for name,data in self.members.items():archive.writestr(name,data+b'changed' if name==member else data)
                with self.assertRaises(ValueError):release.legal.verify_apk(apk,self.root)


if __name__ == "__main__":
    unittest.main()
