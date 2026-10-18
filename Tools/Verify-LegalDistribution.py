"""Check actual APK permissions, offline notices and LGPL corresponding-source integrity."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import shutil
import re
import xml.etree.ElementTree as ET
import zipfile

ANDROID='{http://schemas.android.com/apk/res/android}'
PERMISSIONS={
    'android.permission.INTERNET','android.permission.ACCESS_NETWORK_STATE',
    'android.permission.ACCESS_WIFI_STATE','android.permission.CHANGE_WIFI_MULTICAST_STATE',
    'android.permission.WAKE_LOCK','android.permission.READ_EXTERNAL_STORAGE',
    'android.permission.READ_MEDIA_VIDEO',
}
LIBRARIES={'libavcodec.so','libavutil.so','libswresample.so','libffmpegJNI.so'}


def sha(data):
    return hashlib.sha256(data).hexdigest()


def verify_permissions(text):
    manifest=ET.fromstring(text)
    entries=manifest.findall('uses-permission')+manifest.findall('uses-permission-sdk-23')
    names=[item.get(ANDROID+'name') for item in entries]
    if set(names)!=PERMISSIONS or len(names)!=len(PERMISSIONS):
        raise ValueError('APK permissions differ from the reviewed permission explanation: '+str(sorted(set(names)^PERMISSIONS)))
    storage=next(item for item in entries if item.get(ANDROID+'name')=='android.permission.READ_EXTERNAL_STORAGE')
    if storage.get(ANDROID+'maxSdkVersion')!='32':
        raise ValueError('Legacy media permission must end at Android API 32')
    app=manifest.find('application')
    if app is None or any(app.get(ANDROID+name,'true').lower() not in ('false','0') for name in ('allowBackup','fullBackupContent')):
        raise ValueError('Automatic Android application backup must be disabled')


def verify_apk(apk_path,root):
    legal=root/'Assets/StreamingAssets/Legal'
    binary=json.loads((root/'Native/FFmpeg/binary-manifest.json').read_text())
    lock=json.loads((root/'Native/FFmpeg/sources.lock.json').read_text())
    if binary['license']!='LGPL-2.1-or-later' or binary['linkage']!='shared' or set(binary['libraries'])!=LIBRARIES or binary['sources']!=lock:
        raise ValueError('Unexpected FFmpeg binary configuration')
    catalog=json.loads((root/'Docs/Licenses/RuntimeComponents.json').read_text(encoding='utf-8'))
    if catalog['audioDecoder']!=binary or not catalog['runtimeComponents']:
        raise ValueError('Runtime license catalog is stale or empty')
    for component in catalog['runtimeComponents']:
        if component['id'].startswith('org.jellyfin.') or 'gpl' in component['license'].lower():
            raise ValueError('Unreviewed copyleft dependency in the Android runtime catalog')
        for item in [{'path':component['pom'],'sha256':component['pomSha256']}]+component['notices']:
            if sha((root/item['path']).read_bytes())!=item['sha256']:
                raise ValueError('License metadata or attribution changed: '+item['path'])
    files=['privacy-zh-CN.txt','privacy-en.txt','permissions-zh-CN.txt','permissions-en.txt','notices.txt','RuntimeComponents.json']
    for topic,source in [('privacy','PrivacyPolicy'),('permissions','Permissions')]:
        for language in ('zh-CN','en'):
            canonical=(root/'Docs'/f'{source}.{language}.md').read_text(encoding='utf-8')
            if (legal/f'{topic}-{language}.txt').read_text(encoding='utf-8')!=re.sub(r'^#+\s+','',canonical,flags=re.M):
                raise ValueError('Privacy/permission documents must be synchronized before release')
    with zipfile.ZipFile(apk_path) as apk:
        ass=json.loads((root/'Native/ASS/binary-manifest.json').read_text())
        ass_lock=json.loads((root/'Native/ASS/sources.lock.json').read_text())
        if ass['sources']!=ass_lock or ass['linkage']!='shared' or catalog.get('assRenderer')!=ass:
            raise ValueError('ASS renderer inventory is stale')
        if sha(apk.read('lib/arm64-v8a/libvrplayerass.so'))!=ass['libraries']['libvrplayerass.so']:
            raise ValueError('APK ASS renderer does not match corresponding-source record')
        ass_source=apk.read('assets/Legal/'+ass['sourceArchive'])
        if sha(ass_source)!=ass['sourceArchiveSha256']:
            raise ValueError('APK ASS corresponding-source checksum mismatch')
        with zipfile.ZipFile(io.BytesIO(ass_source)) as archive:
            for name,definition in ass_lock.items():
                if sha(archive.read(name+'-'+definition['version']+'.tar.gz'))!=definition['sha256']:
                    raise ValueError('Incomplete ASS dependency source: '+name)
            for name,expected in ass['rebuildFiles'].items():
                data=archive.read(name)
                if sha(data)!=expected or data!=(root/name).read_bytes().replace(b'\r\n',b'\n'):
                    raise ValueError('ASS rebuild instructions are stale: '+name)
        for name,expected in binary['libraries'].items():
            if sha(apk.read('lib/arm64-v8a/'+name))!=expected:
                raise ValueError('APK audio library does not match corresponding-source record: '+name)
        for name in files:
            if apk.read('assets/Legal/'+name)!=(legal/name).read_bytes():
                raise ValueError('APK legal material is missing or stale: '+name)
            if name.endswith('.txt') and (legal/name).read_bytes()!=(root/'Assets/VRPlayer/Resources/Legal'/name).read_bytes():
                raise ValueError('Visible offline document is stale: '+name)
        if (legal/'RuntimeComponents.json').read_bytes()!=(root/'Docs/Licenses/RuntimeComponents.json').read_bytes():
            raise ValueError('Embedded component catalog is stale')
        source=apk.read('assets/Legal/'+binary['sourceArchive'])
        if sha(source)!=binary['sourceArchiveSha256']:
            raise ValueError('APK corresponding-source archive checksum mismatch')
        with zipfile.ZipFile(io.BytesIO(source)) as archive:
            version=lock['ffmpeg']['version']
            if sha(archive.read('ffmpeg-'+version+'.tar.xz'))!=lock['ffmpeg']['sha256']:
                raise ValueError('Incomplete or incorrect FFmpeg source archive')
            for entry in lock['media3']['files'].values():
                if sha(archive.read('Native/FFmpeg/'+entry['local']))!=entry['sha256']:
                    raise ValueError('Incorrect Media3 wrapper source')
            for name in ['Native/FFmpeg/build.sh','Tools/Build-AudioDecoder.py','Native/FFmpeg/decoder/build.gradle','Native/FFmpeg/sources.lock.json','Native/FFmpeg/README.md']:
                if archive.read(name)!=(root/name).read_bytes().replace(b'\r\n',b'\n'):
                    raise ValueError('Source rebuild instructions are stale: '+name)
    return {'permissions':sorted(PERMISSIONS),'androidBackup':False,'audioLibrarySha256':binary['libraries'],
            'correspondingSourceSha256':binary['sourceArchiveSha256'],'runtimeComponents':len(catalog['runtimeComponents'])}


def copy_public_materials(root,destination):
    sources=[root/'Assets/StreamingAssets/Legal/ffmpeg-lgpl-corresponding-source.zip',
        root/'Assets/StreamingAssets/Legal/ass-corresponding-source.zip',
        root/'Docs/Licenses/THIRD-PARTY-NOTICES.txt',root/'Docs/Licenses/RuntimeComponents.json']
    sources+=[root/'Builds/Compliance'/f'{topic}-{language}.html' for topic in ('privacy','permissions') for language in ('zh-CN','en')]
    for source in sources:
        shutil.copyfile(source,destination/source.name)
    return [source.name for source in sources]


if __name__=='__main__':
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--apk',type=Path,required=True)
    parser.add_argument('--manifest',type=Path,required=True,help='apkanalyzer manifest print output')
    args=parser.parse_args()
    verify_permissions(args.manifest.read_text(encoding='utf-8'))
    print(json.dumps(verify_apk(args.apk,Path(__file__).resolve().parent.parent),indent=2))
