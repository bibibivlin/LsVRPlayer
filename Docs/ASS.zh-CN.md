# ASS / SSA 字幕

播放器使用 [libass 0.17.5](https://github.com/libass/libass/tree/0.17.5) 渲染原始脚本，支持样式、颜色、描边、阴影、位置、移动、旋转、变换、淡入淡出、卡拉 OK、图层、矢量绘图和裁剪。保留 SRT 原有的独立悬浮字幕方式。

## 使用范围

| 来源 | ASS / SSA 路径 |
| --- | --- |
| 本地文件 / SMB | 同目录自动匹配，或主面板与快捷面板手动选择 `.ass` / `.ssa` |
| Emby 外部字幕 | 请求原格式 `Stream.ass` / `Stream.ssa`，沿用请求头认证，不转成 SRT |
| Android MKV 内嵌轨 | 在内嵌字幕列表选择 ASS 轨；保留 codec-private 样式和原始事件 |
| Windows 编辑器 | 外部 ASS / SSA 预览；内嵌轨仍需 Android 播放后端 |

ASS **仅在平面视频画面内部显示**。SBS / TB 平面视频每眼使用同一套画面内脚本坐标，合成跟随影院屏幕的距离、尺寸和曲面。字幕不会越过视频边界，也不会成为独立于视频的悬浮文字。切换 VR180、360、鱼眼、Cubemap 或 EAC 时，ASS 暂停显示；切回平面后按当前位置恢复。双重限制位于 PlayerApp / VideoPresenter 和视频 shader 中。

ASS 使用脚本自身的样式、字号、位置和动画，不应用 SRT 的全局字号、距离、高度、跟随视线或样式预设。字幕设置页会显示相应说明；逐片的“字幕延后”仍生效，正值表示稍后显示。关闭 / 恢复、手动 / 自动模式、同名优先级和影片设置迁移保持原有逻辑。自动候选仍优先中文后缀，其次无后缀，再按文件名排序；同优先级 `.ass` 排在 `.srt` 前，损坏文件继续尝试下一候选。

自带 Noto Sans CJK SC 作为中日韩与拉丁文字回退字体。尚不导入 MKV 附件字体或字幕旁置自定义字体，因此脚本指定字体不可用时会被替换，字形和断行可能与作者环境有差异。其他容器内嵌 ASS、特殊字体依赖和复杂真实片源需单独验证。渲染兼容性由 libass 决定，不承诺与所有 VSFilter 扩展完全一致。

## 构建与资源

`Tools/Build-Native.ps1` 同时构建 ASS 插件。首次使用也可以按 [Native/ASS/README.md](../Native/ASS/README.md) 单独构建。

原生依赖均固定版本和源码 SHA-256。新增 `libvrplayerass.so` 与 `vrplayerass.dll` 均按已有 LFS 规则保存，Unity `.meta` 指定 Android ARM64 / Windows 预览平台。现有字体源与 GUID 不变；StreamingAssets 中额外保存一份相同字体原始字节供 libass 使用。该副本约 16 MiB，会增加 APK 大小。完整源码、许可证与替换说明随 APK 提供，并纳入应用内源码导出和发布校验。详见 [第三方说明](ThirdPartyNotices.md)。

脚本读取 / 解析与样式栅格化异步执行；每个播放器最多有一个渲染任务，旧字幕选择和旧播放代次不能提交图像。libass 字体与图形缓存复用，只上传发生变化的帧，动画最高约 30 次/秒；字幕栅格最大 1920×1080，保持实际显示画面的宽高比。此上限与视频解码分辨率独立。极复杂特效可能降低字幕动画帧率，需要真实片源性能验证。

Android 内嵌 ASS 使用有界滚动事件窗口，保留前 12 秒和后 20 秒，支持现有 ±10 秒字幕延迟；轨道切换与跳转重置窗口。最多 512 个事件 / 2 Mi 字符、1 Mi 字符头部、每包 256 KiB。超出上限的极密集脚本不属于已验证覆盖范围。内嵌原始脚本不写入播放诊断。

## 验证

生成的短片均为公开测试图形，不读取用户电影：

```powershell
python Tools/Generate-AssTestMedia.py
python Tools/Test-AssNative.py --serial <PICO_SERIAL>
python Tools/Device-AssTest.py --serial <PICO_SERIAL> --apk Builds/LsVRPlayer-ASS-development.apk
# 独立测试包可额外传 --package com.linxyi.LsVRPlayer.assvalidation
python Tools/Test-ReleaseVerification.py
```

Unity Test Runner 运行 `VRPlayer.Tests`。`AssSubtitleTests` 覆盖扩展名、匹配、libass 解析、损坏文件回退、取消、投影约束、延迟和 Emby 标识；`AssCompositingTests` 实际渲染视频 shader，检查 Mono / SBS / TB 双眼坐标及全景禁用。Native JUnit 检查原始 ASS 事件时间重封装、图层、样式、逗号和特效标签保留。

`Test-AssNative.py` 在 Windows 和明确指定的 Android 设备上直接调用最终共享库，检查解析、RGBA 通道、裁剪、前后跳转、帧强制刷新、边界限制、移动、淡入淡出、图层透明度及中文字体。其运行不依赖播放器或头显佩戴，不代表 GPU 合成与可读性验收。原生临时目录使用唯一标记验证所有权后清理。

`Device-AssTest.py` 使用现有 development 命令路径操作实际播放器，检查自动外部 ASS、动画、暂停、延迟、五种非平面投影、Off / 恢复、损坏文件、SRT 回归、MKV 内嵌 ASS、前后跳转及停止。全程要求真实 presence、焦点和新鲜诊断；不模拟 presence，不更改 `screen_off_timeout` 或 USB stay-awake。失败后须恢复环境并从头重跑。脚本先备份设置及命令文件，包括原本不存在的情况，最后 force-stop，逐字节恢复并验证；只有唯一标记匹配时才删除本轮夹具目录。清理失败会使整次运行失败。截图和原始记录保存在忽略的 `Docs/Captures` / `Docs/Validation`。

2026-10-18 已完成：Unity **660 / 660 EditMode**；Native **58 / 58 JUnit**；Windows 与指定 PICO 的原生十项合成检查；**21 / 21** 播放器内设备检查；**8 / 8** 发布材料回归及实际 APK 源码 / 二进制完整性校验。完整设备运行采集了 66 次有效 presence / 焦点检查，设置、命令文件和夹具清理全部验证成功。设备原有安装为非调试版，因此在独立包名 `com.linxyi.LsVRPlayer.assvalidation` 的测试副本中执行；原安装和数据未改动。

第一次完整运行发现内嵌 ASS Off 后恢复缺少样式头，修复为每次挂载轨道均从 Format 初始化样式，不依赖 SampleStream 再次发出 FORMAT_READ；新增 JUnit 检查无格式包的重启与换轨隔离后，整轮重新运行通过。早期原生测试另发现 PICO 的冗余 UTF-8 iconv 状态清空崩溃，已用 libass 的原生 UTF-8 输入路径修复并在最终共享库上重测。失败轮次、恢复记录和最终完整通过轮次分别保存在忽略目录，未拼接结果。

以下截图经过隐私检查，仅含生成的测试媒体；分别显示外部 ASS 和 MKV 内嵌 ASS。在双眼中使用相同画面内坐标，文本和绿色绘图均受画面边界约束。

![外部 ASS 合成](Images/ASS/External-Ass.png)

![MKV 内嵌 ASS 合成](Images/ASS/Embedded-Ass.png)

以上是合成短片和自动化验证，不能替代复杂真实字幕、长片稳定性、实体手柄和佩戴舒适度验收。SMB / Emby 的 ASS 文件筛选与原格式读取路径已接入，本轮没有使用私人服务器执行真实网络 ASS 验收。Emby 端点参考[官方字幕接口说明](https://dev.emby.media/doc/restapi/Subtitles.html)。
