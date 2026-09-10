# L's VRPlayer

面向个人 PICO 4 的 Unity VR 视频播放器，当前为 **v0.6.0 开发版**。

- 本地 / SMB2、SMB3 文件浏览和视频播放。
- 普通平面、曲面影院，VR180 / VR360，SBS / OU、Half / Full、交换左右眼和单眼观看。
- 同名及 chs / zh 等语言后缀 SRT、中文编码、字幕同步和位置设置、内嵌文本字幕。
- 手柄射线、虚拟键盘、播放控制、进度恢复、目录连播、收藏、音轨与倍速设置。

## v0.6.0 改进

- SMB 播放增加 1 MiB 按需预读，合并解封装的小块网络请求；信息页增加缓冲余量、加载状态、内存和读取统计，后台记录缓冲事件。保留原缓冲阈值，播放连接支持正常空闲后继续读取。
- 自动加载同目录、同片名 SRT（默认关闭），按中文后缀、无后缀、其他语言排序；记住每部影片的手动选择或关闭状态，支持恢复自动选择。
- 历史记录增加“所在目录”，定位影片时不打断当前播放。
- 左右手摇杆均支持左右快退／快进 10 秒，长拨连续跳转，暂停时保持暂停。
- 点击上一部／下一部切换同文件夹影片时继承当前投影格式；普通打开与自动连播保留影片各自的设置。
- 格式识别结合容器元数据、明确文件名标签和播放期间的画面分析；支持重新识别、建议、撤销和目录 VR180 偏好。设置提供增强识别（默认）、仅标签与元数据、关闭。
- 完整保留 v1 → v2 → v3 设置迁移，升级时保留旧投影设置、历史、收藏及 SMB 配置。

**无标签 VR180 的投影自动判定仍未启用。** 当前画面分析可自动选择 SBS／OU；真实测试集 5 段中 4 段识别为 SBS，VR180 仅给出建议。可手动采用建议，或在确认 SBS 后使用目录 VR180 偏好。SBS 并不等于 VR180；独立真实样片验证未达门槛前不会仅凭画面自动切换 VR180。

## 其他功能

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

正式场景：`Assets/VRPlayer/Scenes/Player.unity`，也是当前唯一加入 Build Settings 的场景。为保持仓库聚焦核心播放器，项目不包含 XRI Toolkit 的 Starter Assets、Device Simulator、DemoScene 或模板 SampleScene；XRI 包本身仍作为 PICO Integration SDK 的传递依赖保留。APK 输出到 `Builds/LsVRPlayer.apk`，仅保存在本地。源码、Unity 资源与 `.meta`、必要的原生插件和字体通过 Git／Git LFS 管理；构建缓存、测试媒体、原始设备记录与凭据不入库。

## 文档与验证

- [安装、操作与构建](Docs/BuildAndUse.zh-CN.md)
- [SMB 预读与缓冲诊断说明](Docs/SmbPlaybackDiagnostics.zh-CN.md)
- [AVC 经 SMB 播放修复验证与剩余验收](Docs/SmbPlaybackValidation.zh-CN.md)
- [v0.6.0 VR180／SBS 识别结果与当前限制](Docs/VR180SbsRecognitionResults.zh-CN.md)
- [字幕、历史、摇杆与格式识别改进记录](Docs/PlaybackImprovements.zh-CN.md)
- [VR180／SBS 识别优化方案](Docs/VR180SbsRecognitionPlan.zh-CN.md)
- [v0.4.0 验证报告与已知边界](Docs/ExperienceValidation.zh-CN.md)
- [v0.3.0 播放内核验证报告](Docs/ValidationReport.zh-CN.md)
- [原始开发计划](Docs/DevelopmentPlan.zh-CN.md)
- [第三方组件与许可](Docs/ThirdPartyNotices.md)

Unity 6000.3.23f1；PICO Integration 3.4.0；Android ARM64 / IL2CPP / GLES3 / Multiview。新增依赖均为免费或开源组件，未使用付费视频插件。

本版已有 EditMode 138/138、原生 JUnit 41/41、PICO 功能回归 24/24（含真实 Wi-Fi SMB）及同目录切片回归 8/8 通过记录。SMB 修复版在问题 AVC 上完成 180 秒真实 NAS 播放，意外缓冲、网络读取错误和解码丢帧均为 0；18 轮切片／seek、字幕及生成的 HEVC／8K 短测通过。头显无人佩戴时会自动暂停，完整三组 10 分钟对照和 60 分钟连续播放仍未完成，不能据短测认定长期稳定性通过；详情见 SMB 修复验证报告。

生成样片成绩不代表真实影片识别准确率，兼容性以报告中的实际样片组合为准。HDR 色调映射、任意鱼眼／立方体布局不属于已支持范围。真实 NAS 的完整认证与加密流程、高码率无线播放、长片热稳定性，以及佩戴后的清晰度和舒适度仍需单独验收。
