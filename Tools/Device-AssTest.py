"""ASS regression on an explicitly selected PICO development APK.

Generate fixtures with Tools/Generate-AssTestMedia.py first. Requires continuous
real headset presence/focus. No simulated presence or screen timeout changes.
Backs up and restores every staged settings/command file, including absence.
"""
import argparse
from datetime import datetime,timezone
import json
from pathlib import Path
import subprocess
import threading
import time
import uuid

ROOT=Path(__file__).resolve().parent.parent
PACKAGE='com.linxyi.LsVRPlayer'
EXTERNAL='/sdcard/Android/data/'+PACKAGE+'/files/'


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--serial',required=True)
    parser.add_argument('--package',default=PACKAGE,help='Optional isolated development application ID')
    parser.add_argument('--apk',type=Path,help='Install this development build after backup')
    parser.add_argument('--adb',default=r'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\PlaybackEngines\AndroidPlayer\SDK\platform-tools\adb.exe')
    args=parser.parse_args()
    global_package=args.package
    if not global_package or any(c not in 'abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789_.' for c in global_package):raise ValueError('Invalid application ID')
    package=global_package
    external='/sdcard/Android/data/'+package+'/files/'
    run=uuid.uuid4().hex
    local=ROOT/'Docs/Validation'/('ass-'+run);local.mkdir(parents=True)
    captures=ROOT/'Docs/Captures'/('ass-'+run);captures.mkdir(parents=True)
    remote=external+'ass-fixture-'+run;staging='/data/local/tmp/vrplayer-ass-'+run
    report={'passed':False,'checks':[],'run':run,'startedUtc':datetime.now(timezone.utc).isoformat(),
            'limitations':['Synthetic short clips; no long-film or subjective worn-headset approval.']}
    saved=[];owned=[];stop_monitor=threading.Event();health=[];thread=None;last_frame=None;last_changed=time.monotonic()
    def adb(*items,check=True):
        return subprocess.run([args.adb,'-s',args.serial,*map(str,items)],check=check,capture_output=True)
    def backup(path,private=False):
        prefix=['run-as',package] if private else []
        exists=adb('shell',*prefix,'test','-e',path,check=False).returncode==0
        data=adb('exec-out',*prefix,'cat',path).stdout if exists else None
        disk=local/(('private-' if private else '')+Path(path).name+'.backup')
        if data is not None:disk.write_bytes(data)
        saved.append((path,private,data,disk))
    def status():return json.loads(adb('exec-out','cat',external+'playback-diagnostics.json').stdout)
    def healthy(value):
        nonlocal last_frame,last_changed
        if not all(value.get(key) is True for key in ('hasFocus','presenceKnown','userPresent')):raise RuntimeError('Keep the headset present and the player focused; interrupted runs must restart')
        stamp=(value.get('timestampUtc'),value.get('frame'))
        if not stamp[0] or stamp[1] is None:raise RuntimeError('Fresh development diagnostics are required')
        age=(datetime.now(timezone.utc)-datetime.fromisoformat(stamp[0].replace('Z','+00:00'))).total_seconds()
        if age>6:raise RuntimeError('Playback diagnostics are stale')
        if stamp!=last_frame:last_frame=stamp;last_changed=time.monotonic()
        if time.monotonic()-last_changed>6:raise RuntimeError('Diagnostics stopped updating')
        if value.get('assError'):raise RuntimeError(value['assError'])
    def wait(read,predicate,timeout=35):
        until=time.monotonic()+timeout
        value=None
        while time.monotonic()<until:
            if health:raise RuntimeError(health[0])
            try:
                value=read()
                if predicate(value):return value
            except (json.JSONDecodeError,subprocess.CalledProcessError):pass
            time.sleep(.25)
        if isinstance(value,dict):report['lastState']=value
        raise RuntimeError('ASS fixture expectation timed out')
    def monitor():
        while not stop_monitor.wait(.5):
            try:healthy(status());report['presenceSamples']=report.get('presenceSamples',0)+1
            except Exception as error:health.append(str(error));return
    def command(action,**fields):
        if health:raise RuntimeError(health[0])
        packet=local/'command.json';packet.write_text(json.dumps(dict(action=action,**fields)),encoding='utf-8')
        adb('shell','run-as',package,'rm','-f','files/command-result.json')
        adb('push',packet,staging+'/command.json')
        adb('shell','run-as',package,'cp',staging+'/command.json','files/command.tmp')
        adb('shell','run-as',package,'mv','files/command.tmp','files/command.json')
        return wait(lambda:json.loads(adb('exec-out','run-as',package,'cat','files/command-result.json').stdout),lambda r:r.get('accepted') and r.get('action')==action)
    def check(name,predicate):
        value=wait(status,predicate);healthy(value);report['checks'].append({'name':name,'state':value});print('PASS '+name,flush=True);return value
    def capture(name):
        file=captures/(name+'.png');file.write_bytes(adb('exec-out','screencap','-p').stdout)
        report.setdefault('captures',[]).append(str(file.relative_to(ROOT)))
    error=None
    try:
        fixtures=ROOT/'TestMedia/ASS'
        for name in ('AssFixture.mp4','AssFixture.ass','AssFixture.srt','Malformed.ass','EmbeddedFixture.mkv'):
            if not (fixtures/name).exists():raise RuntimeError('Generate ASS fixtures first')
        adb('shell','am','force-stop',package)
        if args.apk:adb('install','-r',args.apk)
        adb('shell','run-as',package,'id')
        for name in ('settings.json','settings.json.tmp','playback-diagnostics.json'):backup(external+name)
        for name in ('command.json','command-result.json','command.tmp','command-error.txt'):backup('files/'+name,True)
        adb('shell','mkdir','-p',external)
        adb('shell','run-as',package,'mkdir','-p','files')
        marker=local/'owner.txt';marker.write_text(run)
        adb('shell','mkdir',staging);adb('push',marker,staging+'/owner.txt');owned.append(staging)
        for path,private,_,_ in saved:
            if private:adb('shell','run-as',package,'rm','-f',path)
        for path in (remote,):
            assert path.endswith(run) and path.startswith((external+'ass-fixture-','/data/local/tmp/vrplayer-ass-'))
            adb('shell','mkdir',path);adb('push',marker,path+'/owner.txt');owned.append(path)
        for source in fixtures.iterdir():
            if source.is_file():adb('push',source,remote+'/'+source.name)
        settings=local/'fixture-settings.json'
        settings.write_text(json.dumps({'version':3,'history':[],'favorites':[],'volume':0,'speed':1,'autoHide':False,
                                       'autoLoadSubtitles':True,'formatDetection':1,'loop':True}),encoding='utf-8')
        adb('push',settings,external+'settings.json')
        adb('shell','input','keyevent','KEYCODE_WAKEUP')
        adb('shell','monkey','-p',package,'-c','android.intent.category.LAUNCHER','1')
        print('Player launched. Complete the on-headset privacy notice if shown.',flush=True)
        value=wait(status,lambda s:s.get('snapshot',{}).get('generation')==0 and 'assVisible' in s,timeout=90)
        healthy(value);thread=threading.Thread(target=monitor,daemon=True);thread.start()
        uri='file://'+remote+'/'
        command('open',uri=uri+'AssFixture.mp4',name='AssFixture.mp4')
        check('Automatic external ASS selection',lambda s:s.get('assSelected') and s.get('assVisible'))
        command('pause');command('seek',position=3000);command('menu',flag=False)
        value=check('Paused ASS frame and screen raster',lambda s:s.get('assVisible') and abs(s.get('assPosition',-10000)-3000)<100 and s.get('assWidth')==1920 and s.get('assHeight')==1080)
        capture('external-ass')
        previous=value['assUploadedFrames'];command('play')
        check('Animated frames advance during playback',lambda s:s.get('assUploadedFrames',0)>previous+5)
        command('pause');command('seek',position=3000);command('subtitle-delay',value=1)
        check('Positive delay applies to ASS effects',lambda s:abs(s.get('assPosition',-10000)-2000)<100)
        command('subtitle-delay',value=0)
        for projection in (1,2,3,4,5):
            command('projection',projection=projection,stereo=0)
            check('Projection '+str(projection)+' suspends ASS',lambda s:not s.get('assVisible') and s.get('assSelected') and not s.get('caption'))
        command('projection',projection=0,stereo=0)
        check('Returning to flat resumes ASS',lambda s:s.get('assVisible'))
        command('subtitle-off');check('Off clears ASS overlay',lambda s:not s.get('assVisible') and s.get('subtitleSelection')=='Off')
        command('subtitle-enabled',flag=True);check('Enable restores prior ASS selection',lambda s:s.get('assVisible'))
        command('subtitle',uri=uri+'Malformed.ass');check('Malformed ASS clears previous pixels',lambda s:not s.get('assVisible'))
        command('subtitle',uri=uri+'AssFixture.srt');check('SRT floating captions remain available',lambda s:'SRT regression' in s.get('caption','') and not s.get('assVisible'))
        command('open',uri=uri+'EmbeddedFixture.mkv',name='EmbeddedFixture.mkv')
        value=check('Embedded ASS is selectable',lambda s:s.get('snapshot',{}).get('state')==3 and any(t.get('codec')=='text/x-ssa' for t in s['snapshot'].get('tracks',[])))
        track=next(t['id'] for t in value['snapshot']['tracks'] if t.get('codec')=='text/x-ssa')
        command('track',uri=track,flag=True);command('pause');command('seek',position=3000)
        check('Embedded raw ASS reaches libass',lambda s:s.get('assVisible') and s.get('subtitle')=='track:'+track)
        capture('embedded-ass')
        command('seek',position=8000);check('Embedded forward seek rebuilds events',lambda s:s.get('assVisible') and abs(s.get('assPosition',0)-8000)<100)
        command('seek',position=2000);check('Embedded backward seek rebuilds events',lambda s:s.get('assVisible') and abs(s.get('assPosition',0)-2000)<100)
        command('subtitle-off');check('Embedded Off clears overlay',lambda s:not s.get('assVisible'))
        command('subtitle-enabled',flag=True);check('Embedded enable restores track and effects',lambda s:s.get('assVisible'))
        command('stop');check('Stop clears all ASS pixels',lambda s:not s.get('assVisible') and not s.get('assSelected'))
        report['passed']=True
    except Exception as failure:error=failure;report['error']=str(failure)
    finally:
        stop_monitor.set()
        if thread:thread.join(timeout=3)
        if health:report['passed']=False;report['presenceError']=health[0]
        adb('shell','am','force-stop',package,check=False)
        cleanup=[]
        for path,private,data,disk in reversed(saved):
            try:
                prefix=['run-as',package] if private else []
                if data is None:
                    adb('shell',*prefix,'rm','-f',path)
                    if adb('shell',*prefix,'test','-e',path,check=False).returncode==0:raise RuntimeError('File absence was not restored')
                elif private:
                    temporary=staging+'/restore.bin';adb('push',disk,temporary);adb('shell',*prefix,'cp',temporary,path)
                    if adb('exec-out',*prefix,'cat',path).stdout!=data:raise RuntimeError('Private file bytes differ')
                else:
                    adb('push',disk,path)
                    if adb('exec-out','cat',path).stdout!=data:raise RuntimeError('Settings bytes differ')
            except Exception:cleanup.append('File restoration failed: '+Path(path).name)
        for path in reversed(owned):
            try:
                if adb('exec-out','cat',path+'/owner.txt').stdout.decode()!=run:raise RuntimeError('Ownership marker changed')
                adb('shell','rm','-r',path)
                if adb('shell','test','-e',path,check=False).returncode==0:raise RuntimeError('Owned fixture remains')
            except Exception:cleanup.append('Owned fixture cleanup failed')
        report['restorationVerified']=bool(saved) and not cleanup
        report['cleanupErrors']=cleanup
        if cleanup:report['passed']=False
        (local/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n',encoding='utf-8')
        print('Report: '+str(local/'report.json'),flush=True)
        if not report['passed']:raise RuntimeError('ASS device run failed; inspect preserved report') from error
        print('PASS settings, command files and owned fixtures restored',flush=True)


if __name__=='__main__':main()
