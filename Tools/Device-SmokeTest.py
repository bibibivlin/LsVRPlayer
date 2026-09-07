"""Run the development APK against generated fixtures. Requires an authorized adb device.
The optional SMB fixture is localhost-only via adb reverse; this is not a Wi-Fi/NAS benchmark.
"""
import argparse
import json
import subprocess
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
PACKAGE = 'com.linxyi.LsVRPlayer'
EXTERNAL = '/sdcard/Android/data/' + PACKAGE + '/files/'
parser = argparse.ArgumentParser()
parser.add_argument('--adb', default=r'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe')
parser.add_argument('--smb', action='store_true')
parser.add_argument('--serial', default='')
parser.add_argument('--section', default='all', choices=['all','local','stereo','audio','smb'])
args = parser.parse_args()
report = {'started': time.strftime('%Y-%m-%d %H:%M:%S'), 'checks': []}
output = ROOT / 'Docs' / 'Validation'
output.mkdir(parents=True, exist_ok=True)

def adb(*command, check=True):
    device = ['-s', args.serial] if args.serial else []
    result = subprocess.run([args.adb, *device, *command], capture_output=True, check=check)
    return result.stdout.decode('utf-8', errors='replace')

def wait_for(read, predicate, timeout=25):
    deadline = time.monotonic() + timeout
    last = None
    while time.monotonic() < deadline:
        try:
            last = read()
            if predicate(last):
                return last
        except (json.JSONDecodeError, subprocess.CalledProcessError, FileNotFoundError):
            pass
        time.sleep(.3)
    raise AssertionError('Timed out; last value: ' + str(last))

def command(action, **fields):
    payload = ROOT / 'TestMedia' / 'device-command.json'
    payload.write_text(json.dumps({'action':action, **fields}, ensure_ascii=False), encoding='utf-8')
    adb('shell', 'run-as', PACKAGE, 'rm', '-f', 'files/command-result.json')
    adb('push', str(payload), '/data/local/tmp/vrplayer-command.json')
    adb('shell', 'run-as', PACKAGE, 'cp', '/data/local/tmp/vrplayer-command.json', 'files/command.tmp')
    adb('shell', 'run-as', PACKAGE, 'mv', 'files/command.tmp', 'files/command.json')
    return wait_for(lambda: json.loads(adb('shell', 'run-as', PACKAGE, 'cat', 'files/command-result.json')), lambda x: x.get('accepted') and x.get('action')==action)

def status():
    return json.loads(adb('shell', 'cat', EXTERNAL+'playback-diagnostics.json'))

def record(name, state=None):
    print('PASS ' + name, flush=True)
    report['checks'].append({'name':name, 'passed':True, 'diagnostic':state})
    save()

def save():
    (output / ('device-'+args.section+'.json')).write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')

def capture(name):
    adb('shell', 'screencap', '-p', '/sdcard/vrplayer-screen.png')
    path = ROOT/'Docs'/'Captures'/name
    adb('pull', '/sdcard/vrplayer-screen.png', str(path))
    return path

def open_media(name, base='file:///sdcard/Movies/VRPlayerTest/'):
    previous = status()['snapshot']['generation']
    command('open', uri=base+name, name=name)
    state = wait_for(status, lambda x: x['snapshot']['generation']!=previous and (x['snapshot']['state']==3 or bool(x['snapshot']['error'])))
    assert not state['snapshot']['error'], state
    assert state['snapshot']['width'] > 0, state
    return state

def seek(position):
    command('seek', position=position)
    return wait_for(status, lambda x: abs(x['snapshot']['position']-position)<1000)

def stereo_assert(path, swapped=False):
    from PIL import Image
    with Image.open(path) as picture:
        picture = picture.convert('RGB')
        w,h = picture.size
        counts=[]
        for side in range(2):
            red=blue=0
            for y in range(h//3, 2*h//3, 8):
                for x in range(side*w//2+w//8, side*w//2+3*w//8, 8):
                    r,g,b=picture.getpixel((x,y))
                    red += r>80 and r>b*2 and r>g*2
                    blue += b>80 and b>r*2 and b>g*2
            counts.append((red,blue))
        left,right=counts[::-1] if swapped else counts
        assert left[0]>100 and left[0]>left[1]*3 and right[1]>100 and right[1]>right[0]*3, counts
        return counts

try:
    adb('shell', 'input', 'keyevent', 'KEYCODE_WAKEUP')
    command('repeat', flag=False)
    if args.section in ['all','local']:
        command('browse', uri='file:///sdcard/Movies/VRPlayerTest/')
        time.sleep(2)
        entries=command('inspect')['files']
        assert any(e['name']=='Baseline.2D.mp4' for e in entries)
        record('Local directory enumeration')
        state=open_media('Baseline.2D.mp4')
        assert state['snapshot']['decoder']=='OMX.qcom.video.decoder.avc', state
        record('Local H264 hardware decoding', state)
        command('pause'); time.sleep(2.5)
        before=status()['snapshot']['position']; time.sleep(2.5)
        assert abs(status()['snapshot']['position']-before)<100
        record('Pause keeps media clock stable')
        for name in ['Baseline.2D.chs.srt','Baseline.2D.zh-CN.srt','Baseline.2D.zh.srt']:
            command('subtitle', uri='file:///sdcard/Movies/VRPlayerTest/'+name)
            seek(2000)
            state=wait_for(status, lambda x: x['subtitle'].endswith(name) and '你好' in x['caption'])
            assert len(state['subtitleCandidates'])==3, state
            record('Chinese subtitle '+name, state)
        command('menu', flag=False);command('recenter', flag=True);command('menu', flag=False)
        capture('device-subtitles.png')
        state=seek(22000)
        state=wait_for(status, lambda x: '片尾' in x['caption'])
        record('Backward/forward seek updates subtitle clock', state)
        state=open_media('Baseline.HEVC.mkv')
        assert state['snapshot']['decoder']=='OMX.qcom.video.decoder.hevc', state
        record('Local HEVC MKV hardware decoding', state)
    if args.section in ['all','stereo']:
        for name in ['Stereo.180.SBS.mp4','Stereo.360.OU.mp4']:
            state=open_media(name);command('pause');command('eyes',flag=False);command('recenter',flag=True);command('menu',flag=False);time.sleep(2)
            path=capture('device-'+name+'.png');counts=stereo_assert(path)
            record(name+' separates left red / right blue', {'counts':counts, 'state':state})
            command('eyes',flag=True);time.sleep(1)
            counts=stereo_assert(capture('device-swapped-'+name+'.png'), True)
            record(name+' swaps both eyes', {'counts':counts})
            command('eyes',flag=False)
    if args.section in ['all','audio']:
        for name in ['Audio.ac3.mkv','Audio.eac3.mkv','Audio.dca.mkv','Audio.flac.mkv','Audio.libopus.mkv']:
            open_media(name)
            state=wait_for(status,lambda x: bool(x['snapshot']['audioDecoder']))
            assert any(t['type']==1 and t['selected'] and t['supported'] for t in state['snapshot']['tracks']), state
            record('Audio decoder active: '+name, state)
        state=open_media('Tracks.Subtitles.mkv');command('pause')
        track=next(t for t in state['snapshot']['tracks'] if t['type']==3)
        command('track', uri=track['id'],flag=True);seek(2000)
        state=wait_for(status,lambda x: '你好' in x['caption'])
        record('Embedded SRT track renders Chinese text', state)
        state=open_media('Performance.4K60.HEVC.mp4');time.sleep(7)
        state=status();assert state['snapshot']['width']==3840 and not state['snapshot']['error'],state
        record('Short 4K60 HEVC sample',state)
    if args.smb or args.section=='smb':
        adb('reverse','tcp:1445','tcp:1445')
        result=command('smb',name='Automated localhost fixture',host='127.0.0.1',share='TestMedia',port=1445)
        root=next(e['uri'] for e in result['files'] if e['name']=='Automated localhost fixture')
        command('browse',uri=root);time.sleep(3)
        assert any(e['name']=='Baseline.2D.mp4' for e in command('inspect')['files'])
        record('SMB2 directory enumeration')
        state=open_media('Baseline.2D.mp4',root);record('SMB streamed H264',state)
        command('pause');command('subtitle',uri=root+'Baseline.2D.zh.srt');seek(2000)
        state=wait_for(status,lambda x:'你好' in x['caption']);record('SMB GB18030 subtitle',state)
        seek(25000);seek(1000);record('SMB forward/backward byte seek',status())
        state=open_media('Baseline.HEVC.mkv',root);record('SMB HEVC MKV',state)
    command('pause');command('menu',flag=True)
    report['completed']=time.strftime('%Y-%m-%d %H:%M:%S')
except Exception as exc:
    report['error']=repr(exc);save();raise
finally:
    save()
