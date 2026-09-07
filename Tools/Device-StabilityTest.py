"""Short high-resolution and repeated-open check; not a long thermal endurance test."""
import argparse,json,re,subprocess,time
from pathlib import Path
parser=argparse.ArgumentParser();parser.add_argument('--serial',required=True);parser.add_argument('--cycles',type=int,default=18);args=parser.parse_args()
ROOT=Path(__file__).resolve().parent.parent
ADB=r'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe'
PACKAGE='com.linxyi.LsVRPlayer';EXTERNAL='/sdcard/Android/data/'+PACKAGE+'/files/'
checks=[]
def adb(*items):return subprocess.run([ADB,'-s',args.serial,*items],capture_output=True,check=True).stdout.decode('utf-8')
def status():return json.loads(adb('shell','cat',EXTERNAL+'playback-diagnostics.json'))
def wait(predicate,timeout=25):
    end=time.monotonic()+timeout;last=None
    while time.monotonic()<end:
        try:
            last=status()
            if predicate(last):return last
        except json.JSONDecodeError:pass
        time.sleep(.3)
    raise AssertionError(last)
def command(action,**fields):
    path=ROOT/'TestMedia/device-command.json';path.write_text(json.dumps(dict(action=action,**fields)),encoding='utf-8')
    adb('shell','run-as',PACKAGE,'rm','-f','files/command-result.json')
    adb('push',str(path),'/data/local/tmp/vrplayer-command.json');adb('shell','run-as',PACKAGE,'cp','/data/local/tmp/vrplayer-command.json','files/command.json')
    end=time.monotonic()+25
    while time.monotonic()<end:
        try:
            result=json.loads(adb('shell','run-as',PACKAGE,'cat','files/command-result.json'))
            if result.get('accepted') and result.get('action')==action:return result
        except (subprocess.CalledProcessError,json.JSONDecodeError):pass
        time.sleep(.2)
    raise AssertionError('Command was not consumed; check headset presence and foreground app')
def open_media(name):
    previous=status()['snapshot']['generation'];command('open',name=name,uri='file:///sdcard/Movies/VRPlayerTest/'+name)
    value=wait(lambda x:x['snapshot']['generation']!=previous and (x['snapshot']['state']==3 or bool(x['snapshot']['error'])))
    assert not value['snapshot']['error'],value
    return value
def record(name,**data):
    checks.append(dict(name=name,passed=True,**data));save();print('PASS '+name,flush=True)
def save():(ROOT/'Docs/Validation/device-stability.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2),encoding='utf-8')
def memory():
    text=adb('shell','dumpsys','meminfo',PACKAGE)
    matches=re.findall(r'^\s*TOTAL\s+(\d+)\s',text,re.M)
    return int(matches[0]) if matches else None
try:
    command('repeat',flag=False)
    for name,width in [('Performance.4K60.HEVC.mp4',3840),('Performance.8K30.HEVC.mp4',7680)]:
        initial=open_media(name);command('recenter',flag=True);command('menu',flag=False);time.sleep(4)
        value=status();assert value['snapshot']['width']==width and value['frames']>initial['frames']+30 and not value['snapshot']['error'],value
        adb('shell','screencap','-p','/sdcard/vrplayer-screen.png');adb('pull','/sdcard/vrplayer-screen.png',str(ROOT/'Docs/Captures'/('device-'+name+'.png')))
        record(name,initial=initial,diagnostic=value,pssKB=memory())
    # Warm up both decoder/texture sizes before recording memory baseline.
    sequence=['Baseline.2D.mp4','Baseline.HEVC.mkv','Performance.4K60.HEVC.mp4']
    for name in sequence:open_media(name)
    baseline=memory();samples=[];started=time.monotonic()
    for index in range(args.cycles):
        initial=open_media(sequence[index%len(sequence)]);command('seek',position=4000);command('play');time.sleep(3)
        value=status();assert not value['snapshot']['error'] and value['frames']>initial['frames'],value
        samples.append(dict(index=index,pssKB=memory(),frames=value['frames'],dropped=value['snapshot']['dropped']))
        print('Cycle '+str(index+1)+'/'+str(args.cycles),flush=True)
    final=memory();assert final<baseline+180*1024,dict(baseline=baseline,final=final,samples=samples)
    record('Repeated media open, resolution changes and seek',cycles=args.cycles,seconds=time.monotonic()-started,baselinePssKB=baseline,finalPssKB=final,samples=samples)
    command('repeat',flag=False);command('pause');command('browse',uri='file:///sdcard/Movies/VRPlayerTest/');command('menu',flag=True)
except Exception as exc:checks.append(dict(passed=False,error=repr(exc)));save();raise
