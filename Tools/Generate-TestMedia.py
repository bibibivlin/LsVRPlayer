"""Create small, reproducible media fixtures; no user videos are read."""
from pathlib import Path
import subprocess
import sys
sys.path.insert(0, str(Path(__file__).parent / '.runtime'))
import imageio_ffmpeg

root = Path(__file__).resolve().parent.parent / 'TestMedia'
root.mkdir(exist_ok=True)
ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()

def run(name, *args):
    target = root / name
    if not target.exists():
        subprocess.run([ffmpeg, '-hide_banner', '-loglevel', 'error', '-y', *args, str(target)], check=True)
    print(target)

run('Baseline.2D.mp4', '-f', 'lavfi', '-i', 'testsrc2=size=1920x1080:rate=30:duration=30', '-f', 'lavfi', '-i', 'sine=frequency=440:sample_rate=48000:duration=30', '-c:v', 'libx264', '-preset', 'fast', '-crf', '23', '-pix_fmt', 'yuv420p', '-c:a', 'aac', '-movflags', '+faststart', '-shortest')
run('Stereo.180.SBS.mp4', '-f', 'lavfi', '-i', 'color=c=red:size=960x1080:rate=30:duration=15', '-f', 'lavfi', '-i', 'color=c=blue:size=960x1080:rate=30:duration=15', '-filter_complex', '[0:v][1:v]hstack=inputs=2[v]', '-map', '[v]', '-c:v', 'libx264', '-preset', 'fast', '-crf', '23', '-pix_fmt', 'yuv420p')
run('Baseline.HEVC.mkv', '-i', str(root / 'Baseline.2D.mp4'), '-t', '12', '-c:v', 'libx265', '-preset', 'fast', '-crf', '26', '-c:a', 'aac')
run('Stereo.360.OU.mp4', '-f', 'lavfi', '-i', 'color=c=red:size=1920x540:rate=30:duration=15', '-f', 'lavfi', '-i', 'color=c=blue:size=1920x540:rate=30:duration=15', '-filter_complex', '[0:v][1:v]vstack=inputs=2[v]', '-map', '[v]', '-c:v', 'libx264', '-preset', 'fast', '-pix_fmt', 'yuv420p')
subtitle='1\n00:00:01,000 --> 00:00:05,000\n你好，PICO 4！\nHello, VR cinema.\n\n2\n00:00:07,000 --> 00:00:12,000\n拖动进度后，这条字幕仍应同步。\n\n3\n00:00:20,000 --> 00:00:25,000\n片尾字幕测试。'
for suffix, encoding in [('.chs.srt','utf-8-sig'),('.zh-CN.srt','utf-16'),('.zh.srt','gb18030')]:
    (root / ('Baseline.2D'+suffix)).write_text(subtitle, encoding=encoding)
(root / 'Baseline.2D.sequel.srt').write_text(subtitle, encoding='utf-8')
print('Fixtures ready:', root)
if '--extended' in sys.argv:
    for codec in ['ac3', 'eac3', 'dca', 'flac', 'libopus']:
        run('Audio.'+codec+'.mkv', '-i', str(root/'Baseline.2D.mp4'), '-t', '12', '-c:v', 'copy', '-c:a', codec, '-strict', '-2')
    run('Tracks.Subtitles.mkv', '-i', str(root/'Baseline.2D.mp4'), '-i', str(root/'Baseline.2D.chs.srt'), '-map', '0:v', '-map', '0:a', '-map', '1:0', '-t', '25', '-c:v', 'copy', '-c:a', 'aac', '-c:s', 'srt', '-metadata:s:s:0', 'language=chi')
    run('Performance.4K60.HEVC.mp4', '-f', 'lavfi', '-i', 'testsrc2=size=960x540:rate=60:duration=15', '-vf', 'scale=3840:2160', '-c:v', 'libx265', '-preset', 'ultrafast', '-crf', '28', '-pix_fmt', 'yuv420p', '-an')
if '--8k' in sys.argv:
    run('Performance.8K30.HEVC.mp4', '-f', 'lavfi', '-i', 'testsrc2=size=960x480:rate=30:duration=10', '-vf', 'scale=7680:3840', '-c:v', 'libx265', '-preset', 'ultrafast', '-crf', '28', '-pix_fmt', 'yuv420p', '-an')
if '--large-offset' in sys.argv:
    with (root/'Large-offset.bin').open('wb') as large:
        large.seek((1 << 32) + 4092)
        large.write(b'VRPL')
    print('Generated 4 GB + 4096 byte offset fixture. Remove after the SMB test if space is needed.')
