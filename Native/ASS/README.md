# ASS renderer

The player uses libass 0.17.5, FreeType 2.13.3, FriBidi 1.0.16 and HarfBuzz
10.4.0. Exact source URLs and SHA-256 hashes are in `sources.lock.json`.
No paid library, fontconfig, shell command, external font extraction or network
request is used at playback time. Noto Sans CJK SC is the bundled fallback font;
unavailable script fonts are substituted. Matroska font attachments are not
imported. SSA is also accepted by libass.

Run from the repository root on Windows with Python 3.11+:

```
python -m pip install --target Tools/.runtime/ass-tools meson==1.9.1 ninja==1.13.0
python Tools/Build-Ass.py
```

`Tools/Build-Native.ps1` also runs this build. Use `--platform android` or
`--platform windows` to select one target. `--unity-editor` selects the Unity
installation (6000.3.23f1 by default). Android builds need Unity's Android NDK.
The script downloads and verifies Zig 0.14.1 as a standalone Windows toolchain
and for FriBidi's build-time Unicode table generators. Meson cannot download
additional source dependencies. All cached sources and intermediates live in
`%LOCALAPPDATA%/LsVRPlayer/third-party/ass`, outside the checkout. Upstream
convenience `.wrap` files are removed to prevent conflicting or unpinned
fallback dependencies; runtime source is otherwise unmodified.

Outputs:

- `Assets/Plugins/Android/arm64-v8a/libvrplayerass.so` (Android API 29+, 16 KiB alignment)
- `Assets/Plugins/x86_64/vrplayerass.dll` (Windows Editor preview)
- `Assets/StreamingAssets/Legal/ass-corresponding-source.zip`
- `Native/ASS/binary-manifest.json` (binary/source hashes)

The bridge is a separate shared library. FreeType is used under the FreeType
License, libass under ISC, HarfBuzz under its MIT terms, and FriBidi under
LGPL-2.1-or-later. The corresponding-source archive includes all four complete
upstream source archives, this adapter, source lock, build script and these
instructions. Copyright notices and license texts ship in offline notices.
You may modify the LGPL library and reverse engineer the combination as needed
to debug such modifications. The application's MIT license does not restrict
these rights. To replace the library, rebuild it, replace the `.so` under
`Assets/Plugins/Android/arm64-v8a`, rebuild the APK and sign/install your own
build. No publisher signature or secret is needed for a build under your own
application ID. Keep the `Ass*` C ABI in `AssBridge.c` intact. The Android library
is not linked into Unity, Media3 or the video decoder; do not replace the audio
decoder libraries to update the ASS renderer.

Rendering and script parsing run on a serialized worker. Only changed frames
are uploaded to Unity on the main thread. Raster size is bounded to 1920×1080
(with the displayed video's aspect ratio), libass cache is bounded, and ASS
pixels are composited into the flat video mesh before stereo eye packing.
No GPU call runs on the CPU subtitle worker. Effects cannot leave the displayed
video rectangle, including curved screens and full/half SBS/TB formats.

External SRT keeps its existing floating text path. ASS/SSA retains original
script styling, positioning, animation, clipping, drawing, layering and timing;
only the per-film delay is applied. ASS display is suspended in every immersive
projection and resumes on returning to Flat. Off/restore and playback/selection
generation checks also apply to ASS.

On Android, a dedicated Media3 text renderer preserves raw Matroska ASS packets.
The extractor's timestamps are remuxed without stripping tags. A bounded rolling
script retains 12 seconds of past events (for ±10 second subtitle delay) and
20 seconds of future events. Its maximum is 512 events / 2 Mi characters, with
a 1 Mi character header and 256 KiB packet limit. Script replacements reuse the
libass renderer and fonts; track changes, seeks and playback replacement reset
the stream. Other embedded subtitle types use the existing Media3 path.

Validation and remaining device acceptance are documented in `Docs/ASS.zh-CN.md`.
