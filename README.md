# L's VRPlayer

面向个人 PICO 4 的 Unity VR 视频播放器，当前为 **v0.4.0 开发版**。

- 本地 / SMB2、SMB3 文件浏览和视频播放。
- 普通平面、曲面影院，VR180 / VR360，SBS / OU、Half / Full、交换左右眼和单眼观看。
- 同名及 chs / zh 等语言后缀 SRT、中文编码、字幕同步和位置设置、内嵌文本字幕。
- 手柄射线、虚拟键盘、播放控制、进度恢复、目录连播、收藏、音轨与倍速设置。

## v0.4.0 改进

- 均衡／高清／超清三档渲染倍率，默认高清 1.25×，4× MSAA、72Hz 目标刷新率；提高中文 SDF 字体精度，信息页显示实际眼缓冲与帧率。
- SMB 页自动发现局域网服务器，支持共享枚举、认证及手动配置。
- 文件、投影、字幕、设置、信息五个标签随当前页面正确高亮。
- 字幕距离支持 0.1–6 米，调整距离时保持视觉大小与相对位置。
- 字幕支持四种预设及独立文字颜色、描边、阴影、背景设置，提供实时预览。
- 文件来源与目录使用虚拟滚动列表，支持扳机拖动、摇杆上下滚动和鼠标滚轮。
- 收藏目录可直接移除，并在 5 秒内撤销；旧设置自动迁移，保留历史、收藏与服务器配置。

## 从源码开始

安装 Git LFS 和 Unity **6000.3.23f1**，并为 Unity 安装 Android Build Support（SDK、NDK、OpenJDK）。克隆仓库后在项目根目录执行：

```powershell
git lfs install
git lfs pull
./Tools/Build-Native.ps1
```

用指定版本 Unity 打开项目，等待导入与编译完成，再执行 **VRPlayer → Configure Project and Open Player** 和 **VRPlayer → Build Android APK**。Gradle 首次解析依赖需要网络；Unity 安装路径不同时，为原生构建脚本传入 `-UnityEditor`。

正式场景：`Assets/VRPlayer/Scenes/Player.unity`。APK 输出到 `Builds/LsVRPlayer.apk`，仅保存在本地。源码、Unity 资源与 `.meta`、必要的原生插件和字体通过 Git／Git LFS 管理；构建缓存、测试媒体、原始设备记录与凭据不入库。

## 文档与验证

- [安装、操作与构建](Docs/BuildAndUse.zh-CN.md)
- [v0.4.0 验证报告与已知边界](Docs/ExperienceValidation.zh-CN.md)
- [v0.3.0 播放内核验证报告](Docs/ValidationReport.zh-CN.md)
- [原始开发计划](Docs/DevelopmentPlan.zh-CN.md)
- [第三方组件与许可](Docs/ThirdPartyNotices.md)

Unity 6000.3.23f1；PICO Integration 3.4.0；Android ARM64 / IL2CPP / GLES3 / Multiview。新增依赖均为免费或开源组件，未使用付费视频插件。

本版已有 EditMode 45/45、原生测试 6/6、设备播放回归 24/24 通过记录。兼容性以报告中的实际样片组合为准；HDR 色调映射、任意鱼眼／立方体布局不属于已支持范围。真实 NAS 的完整认证与加密流程、高码率无线播放、长片热稳定性，以及佩戴后的清晰度和舒适度仍需单独验收。
