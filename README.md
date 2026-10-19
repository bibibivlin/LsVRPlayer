# L's VRPlayer

[![Android Build](https://github.com/bibibivlin/LsVRPlayer/actions/workflows/android-release.yml/badge.svg)](https://github.com/bibibivlin/LsVRPlayer/actions/workflows/android-release.yml)
[![release v1.2.0](https://img.shields.io/badge/release-v1.2.0-blue)](https://github.com/bibibivlin/LsVRPlayer/releases/latest)
[![license MIT](https://img.shields.io/badge/license-MIT-blue)](LICENSE)
[![Android 10+](https://img.shields.io/badge/Android-10%2B-3DDC84?logo=android&logoColor=white)](ProjectSettings/ProjectSettings.asset)
[![PICO 4](https://img.shields.io/badge/PICO-4-00B8B0)](Docs/BuildAndUse.zh-CN.md)

面向 **PICO 4** 的 VR 视频播放器，支持本地文件、SMB 共享和 Emby 媒体库。使用 Unity 开发。

[下载 APK](https://github.com/bibibivlin/LsVRPlayer/releases/latest) · [使用说明](Docs/BuildAndUse.zh-CN.md) · [问题反馈](https://github.com/bibibivlin/LsVRPlayer/issues)

## 功能

- **文件来源**：本地存储、SMB2 / SMB3 共享、多个 Emby 服务器与账户；支持局域网发现、搜索、收藏和最近播放。
- **视频显示**：平面 / 曲面屏幕、VR180 / VR360、左右 SBS / 上下 OU；支持 Half / Full、交换左右眼、单眼观看及屏幕距离、全景缩放调节。
- **字幕**：SRT 与内嵌文本字幕；ASS / SSA 使用 libass 保留样式和动画，仅在平面视频画面内显示，支持外部文件和 Android MKV 内嵌轨。
- **Emby**：七种媒体库视图、演员与分类浏览、进度同步、已播放状态，以及同片分 CD 续播和附加篇。
- **播放控制**：完整面板与简易控制条、摇杆快进快退与远近调节、音轨切换、0.25–3× 倍速、单片循环和本地 / SMB 目录连播。
- **逐片记忆**：保存进度、投影、字幕选择、距离和缩放；格式识别结合文件名、元数据与画面分析，支持手动调整和撤销。

## 截图

| Emby 媒体库 | 投影设置 |
| :---: | :---: |
| ![Emby 媒体库](Docs/Images/EmbyUIRefinement/24-EmbyHome.png) | ![投影设置](Docs/Images/UIStyleGrid/02-Projection.png) |

| 字幕样式 | 简易控制条与倍速 |
| :---: | :---: |
| ![字幕样式](Docs/Images/UIStyleGrid/04-SubtitleStyle.png) | ![简易控制条与倍速](Docs/Images/UISliderSpacing/18-QuickSpeed.png) |

截图为实际界面渲染，使用演示数据与合成封面。

## 安装与使用

1. 从 [Releases](https://github.com/bibibivlin/LsVRPlayer/releases/latest) 下载 APK，安装到 PICO 4，从“未知来源”打开。
2. 在“文件”页选择本地视频、配置 SMB 共享，或登录已有的 Emby 账户。
3. 选择影片播放，按需设置投影与字幕。A / X / 菜单键切换完整面板，面板关闭时对空白处按扳机呼出简易控制条，Y / B 返回。

调试版与正式版签名不同，无法直接覆盖安装；切换前请备份数据，卸载会删除应用数据。

## 已知限制

- 视频解码取决于头显能力；Emby 使用原始视频播放，不自动转码。目前不支持 HDR 色调映射。
- 无标签视频的纯画面 VR180 自动投影尚未启用，可手动设置或采用识别建议。SBS 布局不等于 VR180。
- 其他头显尚未确认兼容；长片与高码率无线播放的验证范围见 [SMB](Docs/SmbPlaybackValidation.zh-CN.md) 和 [Emby](Docs/EmbyValidation.zh-CN.md) 记录。

## 文档

- [安装、操作与源码构建](Docs/BuildAndUse.zh-CN.md)
- [中文、英语与日语界面设置](Docs/Localization.zh-CN.md) · [PICO 本地化验收](Docs/LocalizationDeviceValidation.zh-CN.md)
- [Emby 使用说明](Docs/Emby.zh-CN.md)
- [简易控制与全局返回](Docs/QuickControls.zh-CN.md)
- [格式识别说明](Docs/VR180SbsRecognitionResults.zh-CN.md)
- [界面与更多截图](Docs/UIRedesignRelease.zh-CN.md)
- [第三方组件与许可](Docs/ThirdPartyNotices.md)

源码构建需要 Unity **6000.3.23f1**、Android Build Support 与 Git LFS。签名构建和发布见 [发布指南](Docs/Release.zh-CN.md)。

## 参与与许可

问题与 PR 请参阅 [贡献指南](CONTRIBUTING.md) 和 [行为准则](CODE_OF_CONDUCT.md)。安全问题见 [安全政策](SECURITY.md)，版本变化见 [更新日志](CHANGELOG.md)。

项目自有代码与文档采用 [MIT](LICENSE)。新构建使用可重建的 LGPL 共享音频库，随包附带对应源码和第三方声明；旧发布 APK 不受更新影响。第三方组件与商业编解码权利见 [许可说明](Docs/Licensing.zh-CN.md)。应用内可离线查看 [隐私政策](Docs/PrivacyPolicy.zh-CN.md)、[权限说明](Docs/Permissions.zh-CN.md) 和许可，并导出音频库源码。

ASS / SSA 的使用范围、字体限制与验证结果见 [ASS 使用与验证](Docs/ASS.zh-CN.md)。
