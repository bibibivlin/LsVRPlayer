"""Check background pause and process-restart resume on the already installed development APK."""
import json, subprocess, time
from pathlib import Path

ROOT=Path(__file__).resolve().parent.parent
ADB=r'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
import argparse
parser=argparse.ArgumentParser();parser.add_argument('--serial',required=True);args=parser.parse_args()
PACKAGE='com.linxyi.LsVRPlayer'
EXTERNAL='/sdcard/Android/data/'+PACKAGE+'/files/'
checks=[]
def adb(*values):
    return subprocess.run([ADB,'-s',args.serial,*values],capture_output=True,check=True).stdout.decode('utf-8')
def command(action,**kwargs):
    path=ROOT/'TestMedia/device-command.json';path.write_text(json.dumps(dict(action=action,**kwargs)),encoding='utf-8')
    adb('push',str(path),'/data/local/tmp/vrplayer-command.json')
    adb('shell','run-as',PACKAGE,'cp','/data/local/tmp/vrplayer-command.json','files/command.json');time.sleep(1)
def status():return json.loads(adb('shell','cat',EXTERNAL+'playback-diagnostics.json'))
def save(): (ROOT/'Docs/Validation/device-lifecycle.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2),encoding='utf-8')
def passed(name,detail):checks.append(dict(name=name,passed=True,detail=detail));save();print('PASS '+name,flush=True)
def foreground():adb('shell','am','start','-n',PACKAGE+'/com.unity3d.player.UnityPlayerActivity')
try:
    command('repeat',flag=True)
    command('open',uri='file:///sdcard/Movies/VRPlayerTest/Baseline.2D.mp4',name='Baseline.2D.mp4');time.sleep(3)
    command('seek',position=10000);time.sleep(2);before=status()['snapshot']['position']
    adb('shell','input','keyevent','KEYCODE_HOME');time.sleep(8);foreground();time.sleep(4)
    after=status();assert not after['snapshot']['playing'] and abs(after['snapshot']['position']-before)<4000,(before,after)
    passed('Backgrounding pauses video and does not auto-resume',dict(before=before,after=after))
    command('seek',position=9000);command('pause');time.sleep(2)
    settings=json.loads(adb('shell','cat',EXTERNAL+'settings.json'));saved=next(h for h in settings['history'] if h['name']=='Baseline.2D.mp4')
    assert abs(saved['position']-9000)<1000,saved
    adb('shell','am','force-stop',PACKAGE);foreground()
    deadline=time.monotonic()+60
    while time.monotonic()<deadline:
        try:
            if status()['snapshot']['generation']==0:break
        except Exception:pass
        time.sleep(1)
    command('open',uri='file:///sdcard/Movies/VRPlayerTest/Baseline.2D.mp4',name='Baseline.2D.mp4');time.sleep(3)
    resumed=status();assert 8000<resumed['snapshot']['position']<16000,resumed
    assert resumed['subtitle'].endswith('Baseline.2D.zh.srt'),resumed
    passed('Process restart restores position and selected subtitle',resumed)
    command('repeat',flag=False);command('pause');command('menu',flag=True)
except Exception as exc:checks.append(dict(passed=False,error=repr(exc)));save();raise
