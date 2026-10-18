"""Generate public, synthetic ASS/SSA and embedded-MKV fixtures in ignored TestMedia."""
from pathlib import Path
import subprocess
import sys

ROOT=Path(__file__).resolve().parent.parent
sys.path.insert(0,str(ROOT/'Tools/.runtime'))
import imageio_ffmpeg


def main():
    folder=ROOT/'TestMedia/ASS';folder.mkdir(parents=True,exist_ok=True)
    header='''[Script Info]
ScriptType: v4.00+
PlayResX: 1280
PlayResY: 720
ScaledBorderAndShadow: yes
YCbCr Matrix: None
[V4+ Styles]
Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding
Style: Default,Noto Sans CJK SC,48,&H00FFFFFF,&H000000FF,&H00000000,&H80000000,0,0,0,0,100,100,0,0,1,2,1,2,32,32,28,1
[Events]
Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text
'''
    script=header+r'''Dialogue: 0,0:00:00.00,0:00:12.00,Default,,0,0,0,,{\an8\pos(640,40)}ASS 测试 / synthetic fixture
Dialogue: 1,0:00:00.50,0:00:11.50,Default,,0,0,0,,{\an7\move(-160,140,1200,140)\c&H0000FF&\p1}m 0 0 l 240 0 240 60 0 60
Dialogue: 2,0:00:01.00,0:00:11.00,Default,,0,0,0,,{\pos(640,360)\fad(500,500)\t(0,5000,\frz15)\t(5000,10000,\frz-15)}中文样式，旋转与淡入淡出\NStyled text, rotation and fade
Dialogue: 3,0:00:01.00,0:00:11.00,Default,,0,0,0,,{\pos(640,640)}{\k150}卡拉{\k150}OK {\k200}karaoke{\k200} timing
Dialogue: 4,0:00:02.00,0:00:10.00,Default,,0,0,0,,{\an7\pos(1000,500)\clip(1000,500,1280,600)\c&H00FF00&\p1}m 0 0 l 600 0 600 150 0 150
'''
    (folder/'AssFixture.ass').write_text(script,encoding='utf-8',newline='\n')
    (folder/'Malformed.ass').write_text('This is intentionally not an ASS script.\n',encoding='utf-8')
    (folder/'AssFixture.srt').write_text('1\n00:00:00,500 --> 00:00:11,500\nSRT regression 字幕\n',encoding='utf-8')
    ffmpeg=imageio_ffmpeg.get_ffmpeg_exe()
    subprocess.run([ffmpeg,'-y','-f','lavfi','-i','testsrc2=size=1280x720:rate=30','-t','12',
                    '-c:v','libx264','-preset','ultrafast','-crf','24','-pix_fmt','yuv420p','-an','-movflags','+faststart',str(folder/'AssFixture.mp4')],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    subprocess.run([ffmpeg,'-y','-i',str(folder/'AssFixture.mp4'),'-i',str(folder/'AssFixture.ass'),
                    '-map','0:v','-map','1:0','-c','copy','-metadata:s:s:0','language=zho',str(folder/'EmbeddedFixture.mkv')],check=True,stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
    print('Generated 12-second synthetic ASS fixtures in TestMedia/ASS')


if __name__=='__main__':main()
