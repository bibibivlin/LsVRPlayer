"""Extract anonymous local validation frames. Source files are read-only; outputs stay in TestMedia."""
import argparse
import json
from pathlib import Path
import re
import subprocess
import sys

ROOT = Path(__file__).resolve().parent.parent
sys.path.insert(0, str(ROOT / 'Tools/.runtime'))
import imageio_ffmpeg

parser = argparse.ArgumentParser()
parser.add_argument('--source', required=True)
parser.add_argument('--output', default='TestMedia/RealRecognition')
parser.add_argument('--edge', type=int, default=512)
args = parser.parse_args()
source = Path(args.source).resolve()
output = (ROOT / args.output).resolve()
if not output.is_relative_to(ROOT / 'TestMedia'):
    raise ValueError('Validation frames must remain in ignored TestMedia')
output.mkdir(parents=True, exist_ok=True)
ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
samples = []
for index, path in enumerate(sorted(p for p in source.rglob('*') if p.suffix.lower() in ('.mp4', '.mkv', '.mov', '.webm'))):
    probe = subprocess.run([ffmpeg, '-hide_banner', '-i', str(path)], capture_output=True).stderr.decode('utf-8', errors='replace')
    video = next(line for line in probe.splitlines() if 'Video:' in line)
    match = re.search(r'(\d{2,5})x(\d{2,5})', video)
    sw, sh = map(int, match.groups())
    scale = min(1, args.edge / max(sw, sh))
    w, h = int(sw * scale) // 2 * 2, int(sh * scale) // 2 * 2
    frames = []
    for second in (2, 5, 9, 13, 17, 21, 25, 28):
        run = subprocess.run([ffmpeg, '-hide_banner', '-loglevel', 'error', '-ss', str(second), '-i', str(path), '-frames:v', '1', '-vf', f'scale={w}:{h}', '-pix_fmt', 'gray', '-f', 'rawvideo', '-'], capture_output=True)
        if run.returncode or len(run.stdout) != w * h:
            continue
        name = f'sample-{index:03d}-{second:02d}.gray'
        (output / name).write_bytes(run.stdout)
        frames.append(name)
    samples.append({'name': f'sample-{index:03d}', 'width': w, 'height': h, 'stereo': 1, 'projection': 1, 'frames': frames})
    print(f'sample-{index:03d}: source {sw}x{sh}, {len(frames)} frames at {w}x{h}', flush=True)
(output / 'manifest.json').write_text(json.dumps({'samples': samples, 'sourceType': 'user-supplied calibration clips; not independent validation'}, indent=2), encoding='utf-8')
