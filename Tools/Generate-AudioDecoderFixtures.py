"""Generate small, nonprivate compressed sine packets for the isolated Android decoder tests."""
from pathlib import Path
import subprocess
import imageio_ffmpeg

output=Path(__file__).resolve().parent.parent/'Native/FFmpeg/decoder/src/androidTest/assets'
output.mkdir(parents=True,exist_ok=True)
for name,codec in [('ac3','ac3'),('eac3','eac3'),('dts','dca')]:
    subprocess.run([imageio_ffmpeg.get_ffmpeg_exe(),'-hide_banner','-loglevel','error','-y',
        '-f','lavfi','-i','sine=frequency=440:sample_rate=48000:duration=0.1','-ac','2',
        '-c:a',codec,'-strict','-2','-frames:a','1','-f',name,str(output/f'audio-{name}.bin')],check=True)
print('Generated AC3, EAC3 and DTS decoder test packets.')
