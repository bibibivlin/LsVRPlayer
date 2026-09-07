# Repository Guidelines

## Project Structure & Module Organization

- `Assets/VRPlayer/Runtime/`: C# playback, storage, subtitles, rendering, persistence, and world-space UI under the `VRPlayer` namespace.
- `Assets/VRPlayer/Editor/`: project configuration and APK build commands. Keep Runtime, Editor, and Tests assemblies separate.
- `Assets/VRPlayer/Scenes/Player.unity`: production scene; preserve existing Demo/Sample scenes.
- `Assets/VRPlayer/Shaders/` and `Assets/VRPlayer/Resources/Fonts/`: projection shaders and Chinese fonts. Commit Unity assets with their `.meta` files.
- `Native/Android/`: Java Media3/SMBJ bridge; `Native/Rendering/`: C++ GLES texture plugin. Rebuilt binaries belong in `Assets/Plugins/Android/`.
- `Assets/VRPlayer/Tests/EditMode/`: NUnit tests. `Tools/` contains build/device scripts; `Docs/` contains plans, instructions, licenses, and validation records.

## Build, Test, and Development Commands

Use Unity **6000.3.23f1** with Android SDK, NDK, and OpenJDK support. Run commands from the repository root.

- `./Tools/Build-Native.ps1`: builds the Java AAR and ARM64 rendering library. Supply `-UnityEditor` for a different installation path.
- Unity menu **VRPlayer → Configure Project and Open Player**, then **Build Android APK**: configures the project and produces `Builds/LsVRPlayer.apk`. Use Play Mode for editor previews.
- Unity **Test Runner → EditMode → VRPlayer.Tests**: runs the NUnit suite.
- `python Tools/Generate-TestMedia.py --extended`: generates fixtures; requires `imageio-ffmpeg`.
- `python Tools/Device-SmokeTest.py --serial <PICO_SERIAL>`: tests the running development APK with installed fixtures; requires Pillow. See `Docs/BuildAndUse.zh-CN.md` for fixture transfer and SMB setup.

## Coding Style & Naming Conventions

Use four-space indentation, C# PascalCase types/methods, and camelCase fields/locals; use camelCase Java methods. Follow nearby brace placement and avoid unrelated reformatting; no formatter is enforced. Guard Android-only C# with `UNITY_ANDROID && !UNITY_EDITOR`. Keep file/network work asynchronous and GLES calls on the render thread.

## Testing Guidelines

Name tests after observable behavior, such as `PositiveDelayMeansLaterDisplay`; use `[TestCase]` for filename variants. Add regressions for changed parsing, timing, persistence, or seeking behavior. No percentage coverage gate exists. Validate native/rendering changes on PICO hardware, record results in `Docs/Validation/`, and distinguish short fixtures from long-film compatibility. Wait for Unity compilation and check Console before editor automation.

## Commit & Pull Request Guidelines

History contains only `初次签入`; no commit convention is established. Use concise imperative subjects, e.g. `Fix subtitle timing after seek`. Keep commits focused and preserve unrelated working-tree changes. PRs should describe behavior, link relevant issues, list tests/device details, and include screenshots for UI/projection changes.

## Dependencies & Sensitive Data

Use free/open-source additions and update `Docs/ThirdPartyNotices.md`. Keep native and Unity Gradle dependency versions aligned. Never commit SMB credentials, signing keys, generated APKs, `Library/`, `TestMedia/`, or `Tools/.runtime/`.
