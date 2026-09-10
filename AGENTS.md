# Repository Guidelines

## Project Structure & Module Organization

- `Assets/VRPlayer/Runtime/`: C# playback, storage, subtitles, rendering, persistence, and world-space UI under the `VRPlayer` namespace.
- `Assets/VRPlayer/Editor/`: project configuration and APK build commands. Keep Runtime, Editor, and Tests assemblies separate.
- `Assets/VRPlayer/Scenes/Player.unity`: production scene and the only scene in build settings. XRI Toolkit sample scenes/assets are intentionally not copied into this project.
- `Assets/VRPlayer/Shaders/` and `Assets/VRPlayer/Resources/Fonts/`: projection/subtitle shaders and Chinese fonts. Commit Unity assets with their `.meta` files; update existing font assets without changing their GUIDs.
- `Native/Android/`: Java Media3/SMBJ bridge; `Native/Rendering/`: C++ GLES texture plugin. Rebuild after native changes and commit changed runtime binaries in `Assets/Plugins/Android/` through the existing Git LFS rules.
- `Assets/VRPlayer/Tests/EditMode/`: NUnit tests. `Tools/` contains build/device scripts; `Docs/` contains plans, instructions, licenses, and validation records.

## Build, Test, and Development Commands

Use Unity **6000.3.23f1** with Android SDK, NDK, and OpenJDK support. Install Git LFS and run `git lfs pull` after cloning. Run commands from the repository root.

- `./Tools/Build-Native.ps1`: builds the Java AAR and ARM64 rendering library. Supply `-UnityEditor` for a different installation path.
- Unity menu **VRPlayer → Configure Project and Open Player**, then **Build Android APK**: configures the project and produces `Builds/LsVRPlayer.apk`. Use Play Mode for editor previews.
- Unity **Test Runner → EditMode → VRPlayer.Tests**: runs the NUnit suite.
- Native JUnit: run the Unity-bundled Gradle task `testDebugUnitTest` against `Native/Android`; the full PowerShell command is in `Docs/BuildAndUse.zh-CN.md`.
- `python Tools/Generate-TestMedia.py --extended`: generates fixtures; requires `imageio-ffmpeg`.
- `python Tools/Device-SmokeTest.py --serial <PICO_SERIAL>`: tests the running development APK with installed fixtures; requires Pillow. See `Docs/BuildAndUse.zh-CN.md` for fixture transfer and SMB setup.
- `python Tools/Device-ExperienceTest.py --serial <PICO_SERIAL> --host <TEST_PC_IP>`: checks quality, tabs, LAN discovery, shared folders, and cancellation. Use `Tools/Serve-LanTestSmb.py` for a fixture server; LAN testing requires Impacket and zeroconf.
- `python Tools/Device-CaptionTest.py --serial <PICO_SERIAL>`: captures subtitle distance and style cases for visual review. `Tools/Device-StabilityTest.py --serial <PICO_SERIAL> --cycles 18` exercises high-resolution fixtures and repeated playback changes. Keep the headset awake during device tests.
- `python Tools/Device-SmbPlaybackTest.py --serial <PICO_SERIAL> --cases <IGNORED_CASES_JSON> --mode read-ahead --seconds 600`: tests SMB prefetch; use `--mode diagnostics` to disable prefetch for comparison. `baseline` mode requires the preserved original APK. See `Docs/SmbPlaybackDiagnostics.zh-CN.md` for case format and metrics.

## Coding Style & Naming Conventions

Use four-space indentation, C# PascalCase types/methods, and camelCase fields/locals; use camelCase Java methods. Follow nearby brace placement and avoid unrelated reformatting; no formatter is enforced. Guard Android-only C# with `UNITY_ANDROID && !UNITY_EDITOR`. Keep file/network work asynchronous and GLES calls on the render thread.

Preserve the complete v1 → v2 → v3 settings migration, per-film subtitle Auto/Manual/Off states, saved projections, history/favorites, and stable `smb://configurationId/path` identifiers. Keep SMB discovery separate from credentials, cancel obsolete operations, and discard stale callbacks. Keep page selection and file-source state centralized, reuse visible directory rows, and isolate subtitle materials from menu text. Apply eye-buffer resolution only on initialization, XR recovery, or quality changes, not every frame.

Route format changes through `PlayerApp` so UI and diagnostics share manual-lock and undo behavior. Explicit previous/next playback in the same folder inherits format fields; ordinary opens and automatic sequential playback retain per-film behavior. Preserve projection enum values, distinguish unknown fields from explicit Mono, and never equate SBS with VR180. Subtitle loading, metadata probing and frame analysis need independent cancellation and playback-generation checks. Frame sampling must not seek or block playback; validate persistent recognition caches against file metadata and algorithm/profile versions.

Keep SMB playback read-ahead bounded and on the Media3 loader thread. Capture playback sessions in data-source factories, cancel retired connections, and keep probing resources independent. Count actual network reads separately from bytes delivered from cache; aggregate diagnostics off playback threads without media paths or credentials. Preserve finite SMB request deadlines while allowing playback sockets to remain idle with full media buffers. Change load-control thresholds only with separate measurements.

## Testing Guidelines

Name tests after observable behavior, such as `PositiveDelayMeansLaterDisplay`; use `[TestCase]` for filename variants. Add regressions for changed parsing, timing, persistence/migration, seeking, or cancellation behavior. No percentage coverage gate exists. Wait for Unity compilation and check Console before editor automation.

Validate native/rendering changes on PICO hardware. Keep raw records in ignored `Docs/Validation/` and screenshots in ignored `Docs/Captures/`; commit curated Markdown reports under `Docs/` and reviewed, nonprivate screenshots under `Docs/Images/`, with reproducible commands and remaining limitations. Distinguish short fixtures from long-film compatibility, real LAN discovery from `adb reverse` playback tests, and screenshot/automated checks from subjective headset acceptance.

Do not modify PICO `screen_off_timeout` in device scripts: firmware may store `-1`, which Android's shell rejects when restoring. Back up and verify restoration of any temporary player or USB stay-awake settings, even after failed tests. External pauses or stale diagnostics invalidate a continuous playback run; restarting short runs does not satisfy a long-duration acceptance check.

Use `Tools/Device-ImprovementsTest.py`, `Tools/Device-AdjacentPlaybackTest.py`, and `Tools/Device-RecognitionStressTest.py` for the added playback regressions; see the current recognition report for fixture prerequisites and commands. Read real samples only from user-designated locations, store extracted frames in ignored `TestMedia/`, and restore device settings after tests. Keep stereo and VR180 projection approval gates separate in `FormatRecognitionProfile.json`: generated fixtures and calibration samples do not establish independent real-video accuracy. Pure-frame VR180 automatic projection remains disabled pending its validation gate; report coverage, abstentions, and false positives separately.

## Commit & Pull Request Guidelines

Use concise imperative subjects, e.g. `Fix subtitle timing after seek`. Keep commits focused and preserve unrelated working-tree changes. Review staged paths and partial-file changes before committing. PRs should describe behavior, link relevant issues, list tests/device details, and include selected screenshots for UI/projection changes after checking for private content.

## Dependencies & Sensitive Data

Use free/open-source additions and update `Docs/ThirdPartyNotices.md` and required license texts. Keep native and Unity Gradle dependency versions aligned; SMBJ-RPC must exclude its transitive SMBJ version in both dependency declarations.

Never log or commit SMB credentials, device settings backups, or signing keys. Keep generated APKs, native build intermediates, `Library/`, `TestMedia/`, `Tools/.runtime/`, and raw device reports ignored. Preserve required Unity assets, `.meta` files, runtime AAR/SO files, and font sources; do not broadly ignore `Assets/Plugins/` or all binary assets. Keep serialized font `.asset` files under the existing Unity YAML rules and binary fonts/plugins under Git LFS.
