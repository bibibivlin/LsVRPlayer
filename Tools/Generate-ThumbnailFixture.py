"""Generate a small, nonprivate red/green/blue timeline for preview decoder regressions."""
from pathlib import Path
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).parent / '.runtime'))
import imageio_ffmpeg

folder = Path(__file__).resolve().parent.parent / 'TestMedia' / 'Thumbnails'
folder.mkdir(parents=True, exist_ok=True)
target = folder / 'seek-preview.mp4'
subprocess.run([
    imageio_ffmpeg.get_ffmpeg_exe(), '-hide_banner', '-loglevel', 'error', '-y',
    '-f', 'lavfi', '-i', 'color=red:s=640x360:r=24:d=4',
    '-f', 'lavfi', '-i', 'color=lime:s=640x360:r=24:d=4',
    '-f', 'lavfi', '-i', 'color=blue:s=640x360:r=24:d=4',
    '-filter_complex', '[0:v][1:v][2:v]concat=n=3:v=1:a=0[v]', '-map', '[v]',
    '-c:v', 'libx264', '-g', '24', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(target)
], check=True)
print(target)
