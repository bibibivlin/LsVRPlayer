"""Build the Apache Media3 wrapper and LGPL-only FFmpeg ARM64 shared libraries.

Requires the Unity Android NDK, Git for Windows bash, and Python 3.11+.
Downloads are hash-pinned; sources and build instructions accompany every binary release.
"""
import argparse
import ctypes
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / 'Native/FFmpeg'
LOCK = json.loads((SOURCE / 'sources.lock.json').read_text())


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def short_path(path):
    buffer = ctypes.create_unicode_buffer(32768)
    if not ctypes.windll.kernel32.GetShortPathNameW(str(path), buffer, len(buffer)):
        raise RuntimeError('Cannot resolve Windows tool path')
    return buffer.value.replace('\\', '/')


def fetch_sources(cache):
    sources = {}
    for name, item in LOCK.items():
        if name == 'media3':
            folder = cache / ('media3-selected-' + item['version'])
            for remote, definition in item['files'].items():
                local = SOURCE / definition['local']
                if not local.exists() or digest(local) != definition['sha256']:
                    local.parent.mkdir(parents=True,exist_ok=True)
                    urllib.request.urlretrieve('https://raw.githubusercontent.com/androidx/media/' + item['commit'] + '/' + remote,local)
                if digest(local) != definition['sha256']:
                    raise ValueError('Media3 source checksum mismatch: ' + remote)
                destination = folder / remote
                destination.parent.mkdir(parents=True,exist_ok=True)
                shutil.copyfile(local,destination)
            sources[name] = (None,folder)
            continue
        archive = cache / ('ffmpeg-' + item['version'] + '.tar.xz' if name == 'ffmpeg' else 'media3-' + item['version'] + '.tar.gz')
        provided = ROOT / archive.name
        if provided.exists() and digest(provided) == item['sha256']:
            shutil.copyfile(provided, archive)
        if not archive.exists() or digest(archive) != item['sha256']:
            temporary = archive.with_suffix('.download')
            urllib.request.urlretrieve(item['url'], temporary)
            if digest(temporary) != item['sha256']:
                raise ValueError('Source checksum mismatch: ' + name)
            temporary.replace(archive)
        folder = cache / (name + '-' + item['version'] + '-source')
        marker = folder / '.source-sha256'
        if not marker.exists() or marker.read_text() != item['sha256']:
            folder.mkdir(exist_ok=True)
            with tarfile.open(archive) as contents:
                prefix = contents.getmembers()[0].name.split('/')[0] + '/'
                for member in contents.getmembers():
                    if not member.name.startswith(prefix):
                        continue
                    member.name = member.name[len(prefix):]
                    if member.name:
                        contents.extract(member, folder, filter='data')
            marker.write_text(item['sha256'])
        sources[name] = (archive, folder)
    return sources


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity-editor', default=r'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor')
    parser.add_argument('--ndk-path', type=Path, help='Use a standalone Android NDK without a Unity installation')
    args = parser.parse_args()
    android = Path(args.unity_editor) / 'Data/PlaybackEngines/AndroidPlayer'
    ndk = args.ndk_path or android / 'NDK'
    cache = Path(os.environ['LOCALAPPDATA']) / 'LsVRPlayer/third-party/lgpl-audio'
    cache.mkdir(parents=True, exist_ok=True)
    sources = fetch_sources(cache)
    ffmpeg = sources['ffmpeg'][1]
    module = sources['media3'][1] / 'libraries/decoder_ffmpeg/src/main'
    java_dest = SOURCE / 'decoder/src/main/java'
    shutil.copytree(module / 'java', java_dest, dirs_exist_ok=True, ignore=shutil.ignore_patterns('ExperimentalFfmpegVideoRenderer.java'))
    # Only the audio fallback is shipped; never enable FFmpeg software video implicitly.
    (java_dest / 'androidx/media3/decoder/ffmpeg/ExperimentalFfmpegVideoRenderer.java').unlink(missing_ok=True)
    jni = SOURCE / 'ffmpeg_jni.cc'
    shutil.copyfile(module / 'jni/ffmpeg_jni.cc', jni)
    build_key = hashlib.sha256((SOURCE / 'build.sh').read_bytes() + jni.read_bytes() + (SOURCE / 'sources.lock.json').read_bytes()).hexdigest()[:12]
    build = cache / ('build-' + build_key)
    bash = Path(r'C:\Program Files\Git\bin\bash.exe')
    make = ndk / 'prebuilt/windows-x86_64/bin/make.exe'
    output = SOURCE / 'decoder/src/main/jniLibs/arm64-v8a'
    output.mkdir(parents=True, exist_ok=True)
    if not (build / '.complete').exists():
        # Build paths contain no spaces; compiler paths use Windows short names.
        command_file = cache / ('build-' + build_key + '.sh')
        command_file.write_bytes((SOURCE / 'build.sh').read_bytes())
        subprocess.run([str(bash), str(command_file).replace('\\', '/'), str(ffmpeg).replace('\\', '/'),
                        str(build).replace('\\', '/'), short_path(ndk), short_path(make)], check=True)
        (build / '.complete').write_text(build_key)
    for name in ('avcodec', 'avutil', 'swresample'):
        library = build / ('lib' + name) / ('lib' + name + '.so')
        if b'LGPL version 2.1 or later' not in library.read_bytes():
            raise ValueError('Unexpected FFmpeg license: ' + name)
        shutil.copyfile(library, output / library.name)
    compiler = ndk / 'toolchains/llvm/prebuilt/windows-x86_64/bin/clang++.exe'
    # Windows is case-insensitive: FFmpeg's VERSION file otherwise shadows C++ <version>.
    headers = cache / ('headers-' + build_key)
    for library_name in ('libavcodec', 'libavutil', 'libswresample'):
        for base in (ffmpeg, build):
            for header in (base / library_name).rglob('*.h'):
                destination = headers / header.relative_to(base)
                destination.parent.mkdir(parents=True, exist_ok=True)
                shutil.copyfile(header, destination)
    subprocess.run([str(compiler), '--target=aarch64-linux-android29', '-std=c++17', '-fPIC', '-shared', '-O2',
                    '-nostdlib++', '-Wl,-z,max-page-size=16384', '-I' + str(headers),
                    str(jni), '-L' + str(output), '-lavcodec', '-lavutil', '-lswresample', '-llog',
                    '-Wl,-soname,libffmpegJNI.so', '-o', str(output / 'libffmpegJNI.so')], check=True)
    strip = ndk / 'toolchains/llvm/prebuilt/windows-x86_64/bin/llvm-strip.exe'
    for library in output.glob('*.so'):
        subprocess.run([str(strip),'--strip-unneeded',str(library)],check=True)
    assets = SOURCE / 'decoder/src/main/assets/vrplayer-licenses'
    assets.mkdir(parents=True, exist_ok=True)
    for license_name in ('COPYING.LGPLv2.1', 'LICENSE.md'):
        shutil.copyfile(ffmpeg / license_name, assets / ('FFmpeg-' + license_name + '.txt'))
    shutil.copyfile(sources['media3'][1] / 'LICENSE', assets / 'Media3-Apache-2.0.txt')
    bundle_dir = ROOT / 'Builds/ThirdParty'
    bundle_dir.mkdir(parents=True, exist_ok=True)
    bundle = bundle_dir / 'ffmpeg-lgpl-corresponding-source.zip'
    with zipfile.ZipFile(bundle, 'w', zipfile.ZIP_DEFLATED) as archive:
        def add(path, name):
            entry = zipfile.ZipInfo(name, date_time=(1980,1,1,0,0,0))
            entry.external_attr = 0o100644 << 16
            entry.compress_type = zipfile.ZIP_STORED if path.suffix in ('.xz','.gz') else zipfile.ZIP_DEFLATED
            data=path.read_bytes()
            if path.suffix not in ('.xz','.gz'):
                data=data.replace(b'\r\n',b'\n')
            archive.writestr(entry,data)
        for name, (original, _) in sources.items():
            if original is not None:
                add(original, original.name)
        add(ROOT / 'Tools/Build-AudioDecoder.py', 'Tools/Build-AudioDecoder.py')
        if (ROOT/'LICENSE').exists():add(ROOT/'LICENSE','LICENSE')
        for path in sorted(SOURCE.rglob('*')):
            if path.is_file() and path.name != 'binary-manifest.json' and 'build' not in path.relative_to(SOURCE).parts and 'jniLibs' not in path.parts and not ('androidTest' in path.parts and 'assets' in path.parts):
                add(path, 'Native/FFmpeg/' + path.relative_to(SOURCE).as_posix())
    streaming = ROOT / 'Assets/StreamingAssets/Legal'
    streaming.mkdir(parents=True,exist_ok=True)
    shutil.copyfile(bundle,streaming/bundle.name)
    record = {'sources': LOCK, 'license': 'LGPL-2.1-or-later', 'linkage': 'shared', 'abi': 'arm64-v8a',
              'libraries': {path.name: digest(path) for path in sorted(output.glob('*.so'))},
              'sourceArchive': bundle.name, 'sourceArchiveSha256': digest(bundle)}
    (SOURCE / 'binary-manifest.json').write_text(json.dumps(record, indent=2) + '\n',encoding='utf-8',newline='\n')
    print('LGPL audio libraries and corresponding-source bundle verified.', flush=True)


if __name__ == '__main__':
    main()
