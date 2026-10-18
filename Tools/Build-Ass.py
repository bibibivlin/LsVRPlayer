"""Build the pinned libass renderer for Android ARM64 and Windows Editor.

Install build tools in Tools/.runtime/ass-tools:
python -m pip install --target Tools/.runtime/ass-tools meson==1.9.1 ninja==1.13.0
Requires Unity's Android NDK. A hash-pinned Zig toolchain builds Windows code.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tarfile
import urllib.request
import uuid
import zipfile

ROOT = Path(__file__).resolve().parent.parent
SOURCE = ROOT / 'Native/ASS'
TOOLS = ROOT / 'Tools/.runtime/ass-tools'


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def fetch(cache):
    lock = json.loads((SOURCE / 'sources.lock.json').read_text())
    for name, entry in lock.items():
        archive = cache / (name + '-' + entry['version'] + '.tar.gz')
        if not archive.exists() or digest(archive) != entry['sha256']:
            staging = archive.with_suffix('.download')
            urllib.request.urlretrieve(entry['url'], staging)
            if digest(staging) != entry['sha256']:
                raise ValueError('Source checksum mismatch: ' + name)
            staging.replace(archive)
        folder = cache / 'source/subprojects' / name
        marker = folder / '.source-sha256'
        if not marker.exists() or marker.read_text() != entry['sha256']:
            folder.mkdir(parents=True, exist_ok=True)
            with tarfile.open(archive) as contents:
                prefix = contents.getmembers()[0].name.split('/')[0] + '/'
                for member in contents.getmembers():
                    if member.name.startswith(prefix):
                        member.name = member.name[len(prefix):]
                        if member.name:
                            contents.extract(member, folder, filter='data')
            marker.write_text(entry['sha256'])
    # All dependencies are explicitly pinned above. Ignore upstream convenience
    # wraps: they advertise conflicting providers and must never fetch more code.
    for wrap in (cache / 'source/subprojects').rglob('*.wrap'):
        wrap.unlink()
    for name in ('meson.build', 'AssBridge.c'):
        shutil.copyfile(SOURCE / name, cache / 'source' / name)
    return lock


def build(cache, platform, args, env):
    build_dir = cache / ('build-' + platform)
    command = [sys.executable, '-m', 'mesonbuild.mesonmain']
    setup = command + ['setup', str(build_dir), str(cache / 'source'), '--wrap-mode=nodownload']
    native = cache / 'windows.ini'
    zig = str(cache / 'zig-x86_64-windows-0.14.1/zig.exe').replace('\\', '/')
    native.write_text("[binaries]\nc = ['"+zig+"', 'cc']\ncpp = ['"+zig+"', 'c++']\nar = ['"+zig+"', 'ar']\nwindres = ['"+zig+"', 'rc']\n")
    setup += ['--native-file', str(native)]
    if (build_dir / 'meson-private/coredata.dat').exists():
        setup += ['--reconfigure']
    if platform == 'android':
        ndk = Path(args.unity_editor) / 'Data/PlaybackEngines/AndroidPlayer/NDK'
        llvm = ndk / 'toolchains/llvm/prebuilt/windows-x86_64/bin'
        cross = cache / 'android.ini'
        def path(name):
            return str(llvm / name).replace('\\', '/')
        cross.write_text(
            "[binaries]\nc = ['" + path('clang.exe') + "', '--target=aarch64-linux-android29']\n"
            "cpp = ['" + path('clang++.exe') + "', '--target=aarch64-linux-android29']\n"
            "ar = '" + path('llvm-ar.exe') + "'\nstrip = '" + path('llvm-strip.exe') + "'\n"
            "[host_machine]\nsystem = 'android'\ncpu_family = 'aarch64'\ncpu = 'armv8-a'\nendian = 'little'\n"
            "[properties]\nneeds_exe_wrapper = true\n[built-in options]\n"
            "c_link_args = ['-Wl,-z,max-page-size=16384']\n"
            "cpp_link_args = ['-static-libstdc++', '-Wl,-z,max-page-size=16384']\n")
        setup += ['--cross-file', str(cross)]
    subprocess.run(setup, env=env, check=True)
    subprocess.run(command + ['compile', '-C', str(build_dir), '-j', '8'], env=env, check=True)
    filename = 'libvrplayerass.so' if platform == 'android' else 'vrplayerass.dll'
    output = ROOT / ('Assets/Plugins/Android/arm64-v8a' if platform == 'android' else 'Assets/Plugins/x86_64') / filename
    output.parent.mkdir(parents=True, exist_ok=True)
    staging = cache / ('staged-' + filename)
    built=build_dir / ('libvrplayerass.dll' if platform=='windows' else filename)
    shutil.copyfile(built, staging)
    if platform=='android':
        subprocess.run([str(llvm/'llvm-strip.exe'),'--strip-unneeded',str(staging)],check=True)
    if not output.exists() or digest(staging) != digest(output):
        try:
            os.replace(staging, output)
        except PermissionError:
            # Windows maps Editor plugins until process exit. Renaming the old
            # file is allowed even when replacing its mapped inode is denied.
            retired=cache/('retired-'+uuid.uuid4().hex+'-'+filename)
            output.rename(retired)
            try:os.replace(staging,output)
            except BaseException:
                retired.rename(output)
                raise
    else:
        staging.unlink()
    print(platform, filename, digest(output))


def package(cache, lock):
    legal=ROOT/'Assets/StreamingAssets/Legal';legal.mkdir(parents=True,exist_ok=True)
    archive=legal/'ass-corresponding-source.zip'
    rebuild=['Native/ASS/meson.build','Native/ASS/AssBridge.c','Native/ASS/sources.lock.json','Native/ASS/README.md','Tools/Build-Ass.py','Docs/Licenses/ASS/Unicode-3.0.txt']
    with zipfile.ZipFile(archive,'w',compression=zipfile.ZIP_DEFLATED,compresslevel=6) as output:
        for name in sorted(rebuild):
            entry=zipfile.ZipInfo(name,(2026,1,1,0,0,0));entry.compress_type=zipfile.ZIP_DEFLATED
            output.writestr(entry,(ROOT/name).read_bytes().replace(b'\r\n',b'\n'))
        for name,definition in sorted(lock.items()):
            source=cache/(name+'-'+definition['version']+'.tar.gz')
            entry=zipfile.ZipInfo(source.name,(2026,1,1,0,0,0))
            output.writestr(entry,source.read_bytes())
    licenses=ROOT/'Docs/Licenses/ASS';licenses.mkdir(parents=True,exist_ok=True)
    files={'libass-ISC.txt':'libass/COPYING','FreeType-FTL.txt':'freetype/docs/FTL.TXT',
           'FriBidi-LGPL-2.1.txt':'fribidi/COPYING','FriBidi-AUTHORS.txt':'fribidi/AUTHORS',
           'HarfBuzz-MIT.txt':'harfbuzz/COPYING','HarfBuzz-ms-use.txt':'harfbuzz/src/ms-use/COPYING'}
    for destination,source in files.items():
        (licenses/destination).write_text((cache/'source/subprojects'/source).read_text(encoding='utf-8'),encoding='utf-8',newline='\n')
    manifest={'sources':lock,'linkage':'shared','sourceArchive':archive.name,'sourceArchiveSha256':digest(archive),
              'libraries':{},'rebuildFiles':{name:hashlib.sha256((ROOT/name).read_bytes().replace(b'\r\n',b'\n')).hexdigest() for name in rebuild}}
    for path in [ROOT/'Assets/Plugins/Android/arm64-v8a/libvrplayerass.so',ROOT/'Assets/Plugins/x86_64/vrplayerass.dll']:
        if path.exists():manifest['libraries'][path.name]=digest(path)
    (SOURCE/'binary-manifest.json').write_text(json.dumps(manifest,indent=2)+'\n',encoding='utf-8',newline='\n')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity-editor', default=r'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor')
    parser.add_argument('--platform', choices=['android', 'windows', 'all'], default='all')
    args = parser.parse_args()
    if not (TOOLS/'mesonbuild/mesonmain.py').exists() or not list(TOOLS.rglob('ninja.exe')):
        subprocess.run([sys.executable,'-m','pip','install','--target',str(TOOLS),'meson==1.9.1','ninja==1.13.0'],check=True)
    cache = Path(os.environ['LOCALAPPDATA']) / 'LsVRPlayer/third-party/ass'
    cache.mkdir(parents=True, exist_ok=True)
    zig = cache / 'zig-x86_64-windows-0.14.1/zig.exe'
    if not zig.exists():
        archive = cache / 'zig.zip'
        if not archive.exists():
            urllib.request.urlretrieve('https://ziglang.org/download/0.14.1/zig-x86_64-windows-0.14.1.zip', archive)
        if digest(archive) != '554f5378228923ffd558eac35e21af020c73789d87afeabf4bfd16f2e6feed2c':
            raise ValueError('Zig toolchain checksum mismatch')
        with zipfile.ZipFile(archive) as contents:
            contents.extractall(cache)
    lock=fetch(cache)
    env = dict(os.environ)
    env['PYTHONPATH'] = str(TOOLS)
    env['NINJA'] = str(next(TOOLS.rglob('ninja.exe')))
    env['PATH'] = str(Path(env['NINJA']).parent) + os.pathsep + env['PATH']
    for platform in (['android', 'windows'] if args.platform == 'all' else [args.platform]):
        build(cache, platform, args, env)
    package(cache,lock)


if __name__ == '__main__':
    main()
