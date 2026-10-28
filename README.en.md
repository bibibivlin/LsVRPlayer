# L's VRPlayer

[简体中文](README.md) | **English**

[![Android Build](https://github.com/bibibivlin/LsVRPlayer/actions/workflows/android-release.yml/badge.svg)](https://github.com/bibibivlin/LsVRPlayer/actions/workflows/android-release.yml)
[![release v1.3.0](https://img.shields.io/badge/release-v1.3.0-blue)](https://github.com/bibibivlin/LsVRPlayer/releases/latest)
[![license MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![Android 10+](https://img.shields.io/badge/Android-10%2B-3DDC84?logo=android&logoColor=white)](ProjectSettings/ProjectSettings.asset)
[![PICO 4](https://img.shields.io/badge/PICO-4-00B8B0)](Docs/BuildAndUse.zh-CN.md)

A VR video player for **PICO 4**, supporting local files, SMB shares, and Emby libraries. Built with Unity.

[Download APK](https://github.com/bibibivlin/LsVRPlayer/releases/latest) · [User Guide](Docs/BuildAndUse.zh-CN.md) · [Report an Issue](https://github.com/bibibivlin/LsVRPlayer/issues)

## Features

- **File sources**: Local storage, SMB2 / SMB3 shares, and multiple Emby servers and accounts; supports LAN discovery, search, favorites, and recently played items.
- **Video display**: Flat / curved screens, VR180 / VR360, and side-by-side SBS / over-under OU; supports Half / Full, swapping left and right eyes, single-eye viewing, screen distance adjustment, and panorama zoom.
- **Subtitles**: SRT and embedded text subtitles; ASS / SSA subtitles use libass to preserve styles and animations and are displayed only within the picture of flat videos, with support for external files and embedded MKV tracks on Android.
- **Emby**: Seven library views, actor and category browsing, progress sync, watched status, continuation across multiple CD parts of the same film, and extras.
- **Playback controls**: Full panel and quick controls, stick controls for seeking and distance adjustment, audio track switching, 0.25–3× playback speed, single-film loop, and sequential playback in local / SMB folders.
- **Per-film memory**: Saves progress, projection, subtitle selection, distance, and zoom; format recognition combines filenames, metadata, and frame analysis, with manual adjustment and undo.

## Screenshots

| Emby library | Projection settings |
| :---: | :---: |
| ![Emby library](Docs/Images/EmbyUIRefinement/24-EmbyHome.png) | ![Projection settings](Docs/Images/UIStyleGrid/02-Projection.png) |

| Subtitle style | Quick controls and playback speed |
| :---: | :---: |
| ![Subtitle style](Docs/Images/UIStyleGrid/04-SubtitleStyle.png) | ![Quick controls and playback speed](Docs/Images/UISliderSpacing/18-QuickSpeed.png) |

Screenshots show the actual rendered interface using demo data and synthetic cover art.

## Installation and use

1. Download the APK from [Releases](https://github.com/bibibivlin/LsVRPlayer/releases/latest), install it on your PICO 4, and open it from “Unknown Sources”.
2. On the “Files” page, select a local video, configure an SMB share, or sign in to an existing Emby account.
3. Select a video to play and adjust projection and subtitles as needed. Press A / X / Menu to toggle the full panel. When the panel is closed, press the trigger while pointing at an empty area to open the quick controls. Press Y / B to go back.

Debug and release builds use different signing keys and cannot be installed over each other. Back up your data before switching; uninstalling deletes the app's data.

## Known limitations

- Video decoding depends on the headset's capabilities. Emby plays the original video without automatic transcoding. HDR tone mapping is not currently supported.
- Automatic VR180 projection based solely on frame analysis is not yet enabled for untagged videos. You can set it manually or use the recognition suggestions. SBS layout does not imply VR180.
- Compatibility with other headsets has not been confirmed. See the [SMB](Docs/SmbPlaybackValidation.zh-CN.md) and [Emby](Docs/EmbyValidation.zh-CN.md) validation records for the scope of long-film and high-bitrate wireless playback testing.

## Documentation

The following documents are in Chinese.

- [Installation, operation, and building from source](Docs/BuildAndUse.zh-CN.md)
- [Chinese, English, and Japanese interface settings](Docs/Localization.zh-CN.md) · [PICO localization acceptance](Docs/LocalizationDeviceValidation.zh-CN.md)
- [Emby user guide](Docs/Emby.zh-CN.md)
- [Quick controls and global back navigation](Docs/QuickControls.zh-CN.md)
- [Flat-screen cinema backgrounds](Docs/CinemaBackground.zh-CN.md) · [Highlight compression, subtitle brightness, and device regression](Docs/CinemaComfort.zh-CN.md)
- [Format recognition](Docs/VR180SbsRecognitionResults.zh-CN.md)
- [Interface and more screenshots](Docs/UIRedesignRelease.zh-CN.md)
- [Third-party components and licenses](Docs/ThirdPartyNotices.md)

Building from source requires Unity **6000.3.23f1**, Android Build Support, and Git LFS. See the [release guide](Docs/Release.zh-CN.md) for signed builds and publishing.

## Contributing and license

For issues and pull requests, see the [contribution guide](CONTRIBUTING.md) and [code of conduct](CODE_OF_CONDUCT.md). For security issues, see the [security policy](SECURITY.md). Version changes are documented in the [changelog](CHANGELOG.md).

The project's own code and documentation are licensed under [MIT](LICENSE). New builds use a rebuildable LGPL shared audio library, with the corresponding source code and third-party notices included in the package; previously released APKs are unaffected by updates. See the [licensing information](Docs/Licensing.zh-CN.md) for third-party components and commercial codec rights. The [privacy policy](Docs/PrivacyPolicy.zh-CN.md), [permissions information](Docs/Permissions.zh-CN.md), and licenses are available offline in the app, which can also export the audio library source code.

See [ASS usage and validation](Docs/ASS.zh-CN.md) for the scope of ASS / SSA support, font limitations, and validation results.
