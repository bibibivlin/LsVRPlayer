"""Resolve runtime license metadata, preserve supplied notices and generate offline credits.

Unknown licenses, missing attribution, and the retired GPL dependency stop the build.
The inventory is produced by Native/Android's writeRuntimeInventory Gradle task.
"""
import hashlib
import json
from pathlib import Path
import re
import shutil
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

ROOT=Path(__file__).resolve().parent.parent
LICENSES=ROOT/'Docs/Licenses'
POMS=LICENSES/'Poms'


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def pom(group,name,version):
    target=POMS/f'{group}.{name}-{version}.pom'
    if not target.exists():
        relative=group.replace('.','/')+f'/{name}/{version}/{name}-{version}.pom'
        bases=['https://dl.google.com/dl/android/maven2/','https://maven.aliyun.com/repository/google/'] if group.startswith('androidx.') else ['https://repo.maven.apache.org/maven2/','https://maven.aliyun.com/repository/central/']
        for base in bases:
            try:
                with urllib.request.urlopen(base+relative,timeout=20) as response:data=response.read()
                ET.fromstring(data);target.write_bytes(data.replace(b'\r\n',b'\n'));break
            except Exception:
                continue
        else:raise ValueError('Cannot retrieve license metadata: '+group+':'+name+':'+version)
    target.write_bytes(target.read_bytes().replace(b'\r\n',b'\n'))
    document=ET.parse(target).getroot()
    names=[entry.text for entry in document.findall('.//{*}licenses/{*}license/{*}name')]
    if not names:
        parent=document.find('{*}parent')
        if parent is not None:
            names=pom(*(parent.findtext('{*}'+key) for key in ('groupId','artifactId','version')))[1]
    return target,names


def license_family(names):
    value=' '.join(names).lower()
    if 'gpl' in value:raise ValueError('Copyleft runtime dependency needs a separate reviewed distribution path: '+value)
    if 'apache' in value:return 'Apache-2.0'
    if 'bouncy castle' in value or 'mit' in value:return 'MIT'
    if 'bsd-3' in value:return 'BSD-3-Clause'
    raise ValueError('Unreviewed runtime license: '+repr(names))


def main():
    POMS.mkdir(parents=True,exist_ok=True)
    artifacts=LICENSES/'Artifacts';artifacts.mkdir(exist_ok=True)
    inventory=json.loads((ROOT/'Native/Android/build/runtime-inventory.json').read_text())
    # Unity/PICO add these outside the native bridge's Gradle dependency graph.
    pico_folders=list((ROOT/'Library/PackageCache').glob('com.unity.xr.picoxr@*'))
    if len(pico_folders)!=1:raise ValueError('Select the resolved PICO SDK for license auditing')
    gson=pico_folders[0]/'Enterprise/android/gson-2.10.1.jar'
    platform_cache=ROOT/'Tools/.runtime/license-audit';platform_cache.mkdir(parents=True,exist_ok=True)
    frame_files=list((Path.home()/'.gradle/caches/modules-2/files-2.1/androidx.games/games-frame-pacing/2.1.2').glob('*/*.aar'))
    frame=frame_files[0] if frame_files else platform_cache/'games-frame-pacing-2.1.2.aar'
    if not frame.exists():
        relative='androidx/games/games-frame-pacing/2.1.2/games-frame-pacing-2.1.2.aar'
        for base in ('https://dl.google.com/dl/android/maven2/','https://maven.aliyun.com/repository/google/'):
            try:urllib.request.urlretrieve(base+relative,frame);break
            except Exception:continue
        else:raise ValueError('Cannot retrieve Unity frame-pacing license artifact')
    for group,name,version,path,expected in [
        ('com.google.code.gson','gson','2.10.1',gson,'4241c14a7727c34feea6507ec801318a3d4a90f070e4525681079fb94ee4c593'),
        ('androidx.games','games-frame-pacing','2.1.2',frame,'ff4836c77f9b79e447a28d548837c318490bc08f8f74fad6f7597b79ac9997c2')]:
        if digest(path)!=expected:raise ValueError('Review changed Unity/PICO dependency: '+name)
        inventory.append({'group':group,'name':name,'version':version,'file':str(path)})
    inventory.sort(key=lambda item:(item['group'],item['name']))
    components=[];texts={}
    for item in inventory:
        group,name,version=(item[key] for key in ('group','name','version'))
        identity=group+':'+name+':'+version
        if group.startswith('org.jellyfin'):raise ValueError('Retired GPL decoder is still present')
        metadata,names=pom(group,name,version);family=license_family(names)
        supplied=[]
        with zipfile.ZipFile(item['file']) as archive:
            for member in sorted(archive.namelist()):
                if not re.search(r'(?:^|/)(?:license|notice)(?:\.(?:txt|md|html))?$',member,re.I):continue
                text=archive.read(member).decode('utf-8-sig').replace('\r\n','\n').replace('\r','\n')
                filename=f'{group}.{name}-{version}-{Path(member).name}.txt'
                path=artifacts/filename;path.write_text(text,encoding='utf-8',newline='\n');supplied.append(path)
        if name=='mbassador':supplied.append(LICENSES/f'MBassador-{version}-MIT.txt')
        if group=='org.bouncycastle':supplied.append(LICENSES/f'BouncyCastle-{version}-MIT.txt')
        if group=='com.rapid7.client':supplied.append(LICENSES/'SMBJ-RPC-BSD-3-Clause.txt')
        if family=='MIT' and not supplied:raise ValueError('Missing MIT attribution: '+identity)
        for path in supplied:
            text=path.read_text(encoding='utf-8-sig')
            path.write_text(text,encoding='utf-8',newline='\n')
            if family=='MIT' and ('opyright' not in text or 'ermission' not in text):raise ValueError('Incomplete MIT notice: '+identity)
            texts[path.name]=text
        artifact=Path(item['file'])
        components.append({'id':identity,'license':family,'declaredLicense':names,'artifact':artifact.name,'sha256':digest(artifact),
            'pom':metadata.relative_to(ROOT).as_posix(),'pomSha256':digest(metadata),
            'notices':[{'path':path.relative_to(ROOT).as_posix(),'sha256':digest(path)} for path in supplied]})
    # Keep the licenses of fonts and the Unity/PICO runtime-facing packages intact.
    font=ROOT/'Assets/VRPlayer/Resources/Fonts/LICENSE.txt'
    (LICENSES/'NotoSansCJK-OFL-1.1.txt').write_text(font.read_text(encoding='utf-8-sig'),encoding='utf-8',newline='\n')
    texts['NotoSansCJK-OFL-1.1.txt']=font.read_text(encoding='utf-8-sig')
    packages=('com.unity.inputsystem','com.unity.xr.interaction.toolkit','com.unity.xr.core-utils',
              'com.unity.xr.management','com.unity.ugui','com.unity.xr.picoxr')
    package_records=[]
    for package in packages:
        folders=list((ROOT/'Library/PackageCache').glob(package+'@*'))
        if len(folders)!=1:raise ValueError('Select the resolved package for license retention: '+package)
        folder=folders[0];info=json.loads((folder/'package.json').read_text(encoding='utf-8-sig'))
        files=[path for path in folder.iterdir() if path.is_file() and re.search(r'(license|third.party.notices)',path.name,re.I) and path.suffix.lower() in ('.md','.txt')]
        if not files:raise ValueError('Missing Unity/PICO license: '+package)
        kept=[]
        for path in files:
            destination=artifacts/(package+'-'+info['version']+'-'+path.name.replace(' ','-'))
            destination.write_text(path.read_text(encoding='utf-8-sig'),encoding='utf-8',newline='\n');texts[destination.name]=destination.read_text(encoding='utf-8-sig');kept.append(destination.relative_to(ROOT).as_posix())
        bundled=[{'artifact':path.relative_to(folder).as_posix(),'sha256':digest(path)} for path in sorted(folder.rglob('*')) if path.suffix in ('.aar','.jar')]
        package_records.append({'id':package,'version':info['version'],'notices':kept,'bundledArtifacts':bundled})
    ffmpeg_assets=ROOT/'Native/FFmpeg/decoder/src/main/assets/vrplayer-licenses'
    for path in ffmpeg_assets.iterdir():
        if path.is_file():
            (LICENSES/path.name).write_text(path.read_text(encoding='utf-8-sig'),encoding='utf-8',newline='\n');texts[path.name]=path.read_text(encoding='utf-8-sig')
    texts['Apache-2.0.txt']=(LICENSES/'Apache-2.0.txt').read_text(encoding='utf-8-sig')
    texts['LsVRPlayer-MIT.txt']=(ROOT/'LICENSE').read_text(encoding='utf-8-sig')
    catalog={'schemaVersion':1,'runtimeComponents':components,'unityPackages':package_records,
             'audioDecoder':json.loads((ROOT/'Native/FFmpeg/binary-manifest.json').read_text())}
    (LICENSES/'RuntimeComponents.json').write_text(json.dumps(catalog,indent=2,ensure_ascii=False)+'\n',encoding='utf-8',newline='\n')
    header="""L's VRPlayer — 第三方组件与许可 / Third-party notices

本应用使用 FFmpeg 8.1.3 的 LGPL-2.1-or-later 共享音频库，以及 Apache-2.0 的 Media3 1.9.3 音频封装。
This app uses FFmpeg shared audio libraries under LGPL-2.1-or-later and the Apache-2.0 Media3 audio wrapper.

完整 FFmpeg 源码、实际封装源码、版本校验和构建说明随 APK 一起提供。点击“导出源码 / Sources”可保存到 Android Download；也可从 APK 的 assets/Legal/ffmpeg-lgpl-corresponding-source.zip 提取。
Complete corresponding FFmpeg and wrapper sources and build instructions accompany this APK. Export them with Sources, or extract assets/Legal/ffmpeg-lgpl-corresponding-source.zip from the APK.

你可修改 LGPL 库，并为调试这些修改而对组合程序进行必要的逆向工程。本项目自有许可不限制这些权利；Unity/PICO 组件沿用其各自条款。
You may modify the LGPL libraries and reverse engineer the combined work as necessary to debug those modifications. The project's own license does not restrict those rights. Unity/PICO components retain their own terms.

本地/服务器媒体由用户提供，其内容权利不属于本应用。
Media is supplied by users and their servers; media content rights are separate.

运行时组件 / Runtime components
"""
    lines=[header]+[entry['id']+' — '+entry['license'] for entry in components]
    lines+=['\nUnity / PICO runtime packages']+[item['id']+' '+item['version'] for item in package_records]
    for name,text in sorted(texts.items()):lines+=['\n'+name+'\n',text.strip()]
    lines+=['开发者与项目 / Developer and project',
        '[bibibivlin](https://github.com/bibibivlin)','https://github.com/bibibivlin/LsVRPlayer']
    notice='\n\n'.join(lines)+'\n'
    (LICENSES/'THIRD-PARTY-NOTICES.txt').write_text(notice,encoding='utf-8',newline='\n')
    output=ROOT/'Assets/VRPlayer/Resources/Legal';output.mkdir(parents=True,exist_ok=True)
    (output/'notices.txt').write_text(notice,encoding='utf-8',newline='\n')
    streaming=ROOT/'Assets/StreamingAssets/Legal';streaming.mkdir(parents=True,exist_ok=True)
    (streaming/'notices.txt').write_text(notice,encoding='utf-8',newline='\n')
    shutil.copyfile(LICENSES/'RuntimeComponents.json',streaming/'RuntimeComponents.json')
    print(f'Preserved {len(components)} runtime component licenses and {len(package_records)} Unity/PICO package notices.')


if __name__=='__main__':main()
