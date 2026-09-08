# Repository Guidelines

## Project Structure & Module Organization

- `Assets/VRPlayer/Runtime/`: C# playback, storage, subtitles, rendering, persistence, and world-space UI under the `VRPlayer` namespace.
- `Assets/VRPlayer/Editor/`: project configuration and APK build commands. Keep Runtime, Editor, and Tests assemblies separate.
- `Assets/VRPlayer/Scenes/Player.unity`: production scene; preserve existing Demo/Sample scenes.
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

## Coding Style & Naming Conventions

Use four-space indentation, C# PascalCase types/methods, and camelCase fields/locals; use camelCase Java methods. Follow nearby brace placement and avoid unrelated reformatting; no formatter is enforced. Guard Android-only C# with `UNITY_ANDROID && !UNITY_EDITOR`. Keep file/network work asynchronous and GLES calls on the render thread.

Preserve version 2 settings migration, history/favorites, and stable `smb://configurationId/path` identifiers. Keep SMB discovery separate from credentials, cancel obsolete operations, and discard stale callbacks. Keep page selection and file-source state centralized, reuse visible directory rows, and isolate subtitle materials from menu text. Apply eye-buffer resolution only on initialization, XR recovery, or quality changes, not every frame.

## Testing Guidelines

Name tests after observable behavior, such as `PositiveDelayMeansLaterDisplay`; use `[TestCase]` for filename variants. Add regressions for changed parsing, timing, persistence/migration, seeking, or cancellation behavior. No percentage coverage gate exists. Wait for Unity compilation and check Console before editor automation.

Validate native/rendering changes on PICO hardware. Keep raw records in ignored `Docs/Validation/` and screenshots in ignored `Docs/Captures/`; commit curated Markdown reports under `Docs/` with reproducible commands and remaining limitations. Distinguish short fixtures from long-film compatibility, real LAN discovery from `adb reverse` playback tests, and screenshot/automated checks from subjective headset acceptance.

## Commit & Pull Request Guidelines

Use concise imperative subjects, e.g. `Fix subtitle timing after seek`. Keep commits focused and preserve unrelated working-tree changes. Review staged paths and partial-file changes before committing. PRs should describe behavior, link relevant issues, list tests/device details, and include selected screenshots for UI/projection changes after checking for private content.

## Dependencies & Sensitive Data

Use free/open-source additions and update `Docs/ThirdPartyNotices.md` and required license texts. Keep native and Unity Gradle dependency versions aligned; SMBJ-RPC must exclude its transitive SMBJ version in both dependency declarations.

Never log or commit SMB credentials, device settings backups, or signing keys. Keep generated APKs, native build intermediates, `Library/`, `TestMedia/`, `Tools/.runtime/`, and raw device reports ignored. Preserve required Unity assets, `.meta` files, runtime AAR/SO files, and font sources; do not broadly ignore `Assets/Plugins/` or all binary assets. Keep serialized font `.asset` files under the existing Unity YAML rules and binary fonts/plugins under Git LFS.
