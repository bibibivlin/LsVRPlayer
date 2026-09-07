# L's VRPlayer

面向个人 PICO 4 的 Unity VR 视频播放器，当前为 **v0.3.0 核心可用开发版**。

- 本地 / SMB2、SMB3 文件浏览和视频播放。
- 普通平面、曲面影院，VR180 / VR360，SBS / OU、Half / Full、交换左右眼和单眼观看。
- 同名及 chs / zh 等语言后缀 SRT、中文编码、字幕同步和位置设置、内嵌文本字幕。
- 手柄射线、虚拟键盘、播放控制、进度恢复、目录连播、收藏、音轨与倍速设置。

正式场景：`Assets/VRPlayer/Scenes/Player.unity`。本机构建产物：`Builds/LsVRPlayer.apk`（构建产物不进入 Git）。

- [安装、操作与构建](Docs/BuildAndUse.zh-CN.md)
- [验证报告与已知边界](Docs/ValidationReport.zh-CN.md)
- [原始开发计划](Docs/DevelopmentPlan.zh-CN.md)
- [第三方组件与许可](Docs/ThirdPartyNotices.md)

Unity 6000.3.23f1；PICO Integration 3.4.0；Android ARM64 / IL2CPP / GLES3 / Multiview。新增依赖均为免费或开源组件，未使用付费视频插件。兼容性以报告中的实际样片组合为准；HDR、任意鱼眼/立方体布局及长时间热稳定尚未完成验收。
