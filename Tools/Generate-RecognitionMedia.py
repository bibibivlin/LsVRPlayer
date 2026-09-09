"""Generate independently labelled textures, moving stereo/negative clips and VR projection fixtures.

No user media is read. Raw generated files remain in ignored TestMedia/Recognition.
"""
from functools import lru_cache
import argparse
import json
import math
from pathlib import Path
import random
import struct
import subprocess
import sys

sys.path.insert(0, str(Path(__file__).parent / '.runtime'))
from PIL import Image, ImageDraw, ImageFilter, ImageChops
import imageio_ffmpeg

parser = argparse.ArgumentParser()
parser.add_argument('--clips', action='store_true')
parser.add_argument('--metadata-only', action='store_true', help='Create container metadata fixtures from existing generated clips.')
parser.add_argument('--seed', type=int, default=90210, help='Keep a separate seed for a held-out synthetic check after freezing the algorithm.')
parser.add_argument('--output', default='TestMedia/Recognition')
args = parser.parse_args()
workspace = Path(__file__).resolve().parent.parent
root = (workspace / args.output).resolve()
if not root.is_relative_to(workspace / 'TestMedia'):
    raise ValueError('Generated fixtures must remain in ignored TestMedia')
root.mkdir(parents=True, exist_ok=True)
ffmpeg = imageio_ffmpeg.get_ffmpeg_exe()
width, height = 512, 288


def metadata_fixtures():
    def atom(kind, data):
        return struct.pack('>I4s', len(data) + 8, kind) + data
    projection = atom(b'st3d', bytes(5)) + atom(b'sv3d', atom(b'proj', atom(b'prhd', bytes(16)) + atom(b'equi', bytes(20))))
    def rewrite(data):
        output = bytearray()
        at = 0
        while at < len(data):
            size, kind = struct.unpack_from('>I4s', data, at)
            if size < 8 or at + size > len(data):
                raise ValueError('Invalid generated MP4 box')
            body = data[at + 8:at + size]
            if kind in (b'moov', b'trak', b'mdia', b'minf', b'stbl'):
                body = rewrite(body)
            elif kind == b'stsd':
                body = body[:8] + rewrite(body[8:])
            elif kind == b'avc1':
                body += projection
            output.extend(atom(kind, body))
            at += size
        return output
    temporary = root / 'metadata-source.mp4'
    subprocess.run([ffmpeg, '-hide_banner', '-loglevel', 'error', '-y', '-i', str(root / 'Grid.360.mp4'), '-vf', 'scale=512:256', '-c:v', 'libx264', '-preset', 'fast', '-an', str(temporary)], check=True)
    # Default FFmpeg MP4 writes moov after mdat, so increasing moov leaves chunk offsets intact.
    source = temporary.read_bytes()
    at = 0
    boxes = []
    while at < len(source):
        size, kind = struct.unpack_from('>I4s', source, at)
        boxes.append(kind)
        at += size
    assert boxes.index(b'moov') > boxes.index(b'mdat')
    (root / 'Unmarked-Metadata.mp4').write_bytes(rewrite(source))
    subprocess.run([ffmpeg, '-hide_banner', '-loglevel', 'error', '-y', '-i', str(root / 'Unmarked-B.mp4'), '-c', 'copy', '-metadata:s:v:0', 'stereo_mode=top_bottom', str(root / 'Container-B.mkv')], check=True)
    subprocess.run([ffmpeg, '-hide_banner', '-loglevel', 'error', '-y', '-i', str(root / 'Ordinary-2to1.mp4'), '-vf', 'scale=512:256', '-c:v', 'libx264', '-preset', 'fast', str(root / 'Plain-Wide.mp4')], check=True)
    print('Wrote MP4 projection, MKV stereo and plain 2:1 fixtures', flush=True)


if args.metadata_only:
    metadata_fixtures()
    raise SystemExit(0)


@lru_cache(maxsize=12)
def texture(seed, size):
    rng = random.Random(seed)
    # Coloured tiles and overlapping textured surfaces, distinct from NUnit's independent pixels.
    image = Image.new('RGB', size)
    pixels = image.load()
    for y in range(size[1]):
        for x in range(size[0]):
            base = 45 + ((x // 19 + y // 13 + seed) % 7) * 20
            pixels[x, y] = tuple(max(0, min(255, base + rng.randrange(-40, 41) + c)) for c in (15, -10, 5))
    draw = ImageDraw.Draw(image)
    for i in range(25):
        x, y = rng.randrange(size[0]), rng.randrange(size[1])
        draw.ellipse((x, y, x + rng.randrange(8, 35), y + rng.randrange(8, 35)), outline=(220, 180, 100), width=2)
    return image


def frame(seed, layout, variant, time=0):
    eye_size = (width // 2, height) if layout == 1 else (width, height // 2) if layout == 2 else (width, height)
    base = texture(seed, eye_size)
    left = ImageChops.offset(base, time * 2, 0)
    right = ImageChops.offset(left, 6 if layout == 1 else 12, 0)
    if variant == 'mirror':
        right = left.transpose(Image.Transpose.FLIP_LEFT_RIGHT)
    elif variant == 'split':
        right = texture(seed + 8181, eye_size)
    elif variant == 'duplicate':
        right = left
    image = Image.new('RGB', (width, height))
    image.paste(left, (0, 0))
    if layout:
        image.paste(right, (eye_size[0], 0) if layout == 1 else (0, eye_size[1]))
    if variant == 'black':
        image = Image.new('RGB', (width, height))
    return image


manifest = {'width': width, 'height': height, 'seed': args.seed, 'samples': []}
for index in range(84):
    if index < 56:
        layout, variant, expected = 1 + index % 2, 'stereo', 1 + index % 2
    else:
        layout = index % 3
        variant = ['mirror', 'split', 'duplicate', 'black'][index % 4]
        expected = 0
    files = []
    for t in range(3):
        image = frame(args.seed + index * 17, layout, variant, t + 1)
        filename = f'validation-{index:03d}-{t}.gray'
        (root / filename).write_bytes(image.convert('L').tobytes())
        files.append(filename)
    manifest['samples'].append({'name': f'{index:03d}-{variant}', 'stereo': expected, 'frames': files})
(root / 'manifest.json').write_text(json.dumps(manifest, indent=2), encoding='utf-8')
print(f'Wrote {len(manifest["samples"])} synthetic samples; seed={args.seed}', flush=True)


def encode(name, frames, fps):
    output = root / name
    command = [ffmpeg, '-hide_banner', '-loglevel', 'error', '-y', '-f', 'rawvideo', '-pix_fmt', 'rgb24', '-s', f'{width}x{height}', '-r', str(fps), '-i', '-', '-an', '-c:v', 'libx264', '-preset', 'fast', '-crf', '18', '-pix_fmt', 'yuv420p', '-movflags', '+faststart', str(output)]
    with subprocess.Popen(command, stdin=subprocess.PIPE) as process:
        for image in frames:
            process.stdin.write(image.tobytes())
        process.stdin.close()
        if process.wait():
            raise RuntimeError('Fixture encode failed')
    print(output.name, flush=True)


def project(kind, tick):
    image = Image.new('RGB', (width, height), (8, 8, 10))
    draw = ImageDraw.Draw(image)
    if kind == 'fisheye':
        r = height // 2 - 4
        draw.ellipse((width // 2 - r, 4, width // 2 + r, height - 4), fill=(55, 80, 140), outline='white', width=3)
        for radius in range(20, r, 25):
            draw.ellipse((width // 2 - radius, height // 2 - radius, width // 2 + radius, height // 2 + radius), outline='yellow')
    else:
        for x in range(0, width, 32):
            draw.line((x, 0, x, height), fill=(30 + x % 200, 130, 180), width=2)
        for y in range(0, height, 24):
            draw.line((0, y, width, y), fill=(190, 100 + y % 100, 40), width=2)
    draw.text((40 + tick % 180, 90), kind + ' generated grid', fill='white')
    return image


if args.clips:
    for name, layout, variant in [('Unmarked-A.mp4', 1, 'stereo'), ('Unmarked-B.mp4', 2, 'stereo'), ('Ordinary-2to1.mp4', 0, 'stereo'), ('Split-Screen.mp4', 1, 'split'), ('Mirror.mp4', 1, 'mirror'), ('Black-Intro.mp4', 1, 'stereo')]:
        encode(name, (Image.new('RGB', (width, height)) if name.startswith('Black') and t < 60 else frame(731, layout, variant, t // 5) for t in range(180)), 15)
    for kind, name in [('180', 'Grid.180.SBS.mp4'), ('360', 'Grid.360.mp4'), ('fisheye', 'Grid.Fisheye.mp4')]:
        encode(name, (project(kind, t) for t in range(60)), 5)
    subtitle = '1\n00:00:00,000 --> 00:00:11,000\nChinese automatic subtitle\n自动中文字幕\n'
    for suffix in ['chs', 'cht', 'en', '']:
        (root / ('Unmarked-A' + ('.' + suffix if suffix else '') + '.srt')).write_text(subtitle if suffix != 'en' else subtitle.replace('自动中文字幕', 'English subtitle'), encoding='utf-8-sig')
    (root / 'Unmarked-B.chs.srt').write_text('broken subtitle', encoding='utf-8')
    (root / 'Unmarked-B.srt').write_text(subtitle, encoding='utf-8')
    metadata_fixtures()
