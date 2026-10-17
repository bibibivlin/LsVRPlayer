# Reproducible LGPL audio decoder

This replaces the GPL Jellyfin prebuilt decoder for newly built APKs. It does not change any previously released APK.

The unmodified Media3 1.9.3 audio wrapper is Apache-2.0. FFmpeg 8.1.3 is configured with `--disable-gpl --disable-nonfree --disable-version3`, no external codec libraries, no software video decoders, and separate `libavcodec.so`, `libavutil.so`, `libswresample.so` shared libraries. The experimental Media3 software-video renderer is deliberately excluded. Hardware video decoding and the existing audio fallback order are preserved.

`sources.lock.json` pins the full FFmpeg archive by HTTPS URL and SHA-256, and every used Media3 wrapper file by upstream Git commit and SHA-256. The full FFmpeg archive, all used wrapper sources, their license, this directory and build tool are included in `ffmpeg-lgpl-corresponding-source.zip`. Unused Media3 test movies are not included. No FFmpeg source patch is applied. Only generated Windows make include paths are normalized; FFmpeg's source remains unchanged. The minimal audio build does not build/run host generators; the host compiler option supplies compile-only feature probes.

## Build or replace the libraries

On Windows install Git for Windows and an Android NDK with its LLVM toolchain and GNU make (the project uses Unity's bundled NDK). Python 3.11+ is required; on Python 3.11 use 3.11.8 or newer for safe tar extraction.

From the extracted source bundle, run:

```powershell
python Tools/Build-AudioDecoder.py --ndk-path <Android-NDK-directory>
```

The default also works from this repository with Unity 6000.3.23f1. If the original archives are beside this README's `Native` parent, they are used without downloading again. `build.sh` records all configure options. The Android target is ARM64 / API 29, with 16 KB ELF segment alignment. Modify the FFmpeg source/configuration as needed when exercising LGPL rights, rebuild the three FFmpeg shared libraries, and rebuild/relink the Apache JNI wrapper if its ABI changes. The wrapper is dynamically linked and does not embed FFmpeg static archives.

The APK carries the complete source bundle at `assets/Legal/ffmpeg-lgpl-corresponding-source.zip`. The app's Information → Licenses page can export it to Android Downloads without media/storage permission. It can also be extracted with any APK/ZIP tool. Source archives are also supplied beside release APKs. Users may modify the LGPL libraries and reverse engineer the combined work as necessary to debug modifications to those libraries. Nothing in the project's own license limits those rights. A modified APK needs its own Android signing certificate and may require uninstalling the original package before installation; back up personal app data first. PICO platform distribution/entitlement is independently managed by PICO.

`binary-manifest.json` records actual library hashes and the matching source archive hash. Release verification compares these hashes with the APK, checks the embedded source archive, and rejects the retired GPL dependency. The original LGPL-2.1 text and FFmpeg license details accompany the library in the AAR and app notices.

These are software copyright/license distribution materials. A copyright license does not itself grant third-party codec patent rights; commercial distribution in selected regions must separately address any applicable rights. Do not claim that choosing LGPL alone settles commercial codec patent licensing. See https://ffmpeg.org/legal.html.
