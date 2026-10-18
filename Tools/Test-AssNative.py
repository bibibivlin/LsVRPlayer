"""Run the synthetic libass smoke test on Windows and optionally an explicit Android device."""
import argparse
import json
import os
from pathlib import Path
import shutil
import subprocess
import uuid

ROOT=Path(__file__).resolve().parent.parent


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--serial')
    parser.add_argument('--unity-editor',default=r'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor')
    args=parser.parse_args()
    cache=Path(os.environ['LOCALAPPDATA'])/'LsVRPlayer/third-party/ass'
    work=ROOT/'Tools/.runtime/ass-smoke';work.mkdir(parents=True,exist_ok=True)
    font=ROOT/'Assets/StreamingAssets/AssFonts/NotoSansCJKsc-Regular.otf'
    subprocess.run([str(cache/'zig-x86_64-windows-0.14.1/zig.exe'),'cc',str(ROOT/'Native/ASS/AssSmoke.c'),
                    '-L'+str(ROOT/'Assets/Plugins/x86_64'),'-lvrplayerass','-o',str(work/'ass-smoke.exe')],check=True)
    # The import library records Meson's MinGW output name; Unity uses vrplayerass.dll.
    shutil.copyfile(ROOT/'Assets/Plugins/x86_64/vrplayerass.dll',work/'libvrplayerass.dll')
    windows=subprocess.run([str(work/'ass-smoke.exe'),str(font)],check=True,capture_output=True,text=True)
    report={'windows':json.loads(windows.stdout)}
    if args.serial:
        android=Path(args.unity_editor)/'Data/PlaybackEngines/AndroidPlayer'
        clang=android/'NDK/toolchains/llvm/prebuilt/windows-x86_64/bin/clang.exe'
        subprocess.run([str(clang),'--target=aarch64-linux-android29',str(ROOT/'Native/ASS/AssSmoke.c'),
                        '-L'+str(ROOT/'Assets/Plugins/Android/arm64-v8a'),'-lvrplayerass','-Wl,-rpath,$ORIGIN',
                        '-o',str(work/'ass-smoke')],check=True)
        def adb(*items,check=True):return subprocess.run([str(android/'SDK/platform-tools/adb.exe'),'-s',args.serial,*items],capture_output=True,check=check)
        run=uuid.uuid4().hex;remote='/data/local/tmp/vrplayer-ass-native-'+run
        assert remote.startswith('/data/local/tmp/vrplayer-ass-native-') and remote.rsplit('/',1)[-1].endswith(run)
        adb('shell','mkdir',remote)
        owner=work/'owner.txt';owner.write_text(run)
        try:
            adb('push',str(owner),remote+'/owner.txt')
            adb('push',str(work/'ass-smoke'),remote+'/ass-smoke')
            adb('push',str(ROOT/'Assets/Plugins/Android/arm64-v8a/libvrplayerass.so'),remote+'/libvrplayerass.so')
            adb('push',str(font),remote+'/font.otf');adb('shell','chmod','700',remote+'/ass-smoke')
            result=adb('shell',remote+'/ass-smoke',remote+'/font.otf')
            report['android']=json.loads(result.stdout)
        finally:
            if adb('exec-out','cat',remote+'/owner.txt').stdout.decode()!=run:raise RuntimeError('Native test directory ownership changed')
            adb('shell','rm','-r',remote)
            if adb('shell','test','-e',remote,check=False).returncode==0:raise RuntimeError('Native fixture cleanup failed')
            report['cleanupVerified']=True
    destination=ROOT/'Docs/Validation/ass-native.json';destination.parent.mkdir(parents=True,exist_ok=True)
    destination.write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report))


if __name__=='__main__':main()
