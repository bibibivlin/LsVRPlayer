# A 方案视觉细化

日期：2026-10-14。此记录接续 [第一版实施记录](UIRedesignImplementation.zh-CN.md)。以下图片由真实 uGUI 组件、字体和 Shader 渲染，使用合成影片名称与指标，不是 AI 概念图。

后续将通用操作改为图标并将顶部标签相对完整窗口居中，最新预览与验证见 [操作图标与顶部居中](UIIconsAndNavigation.zh-CN.md)。本页保留上一轮视觉细化的截图与测试结果。

## 对照与落地

| 概念图中的效果 | 当前实现 |
| --- | --- |
| 蓝黑面板、顶部环境光、细亮边与柔和阴影 | 共用 `VRPlayer/UI Surface` 材质；在面板自身坐标中计算渐变、局部漫光、圆角轮廓和阴影 |
| 青色选中卡片和白色播放圆盘 | 选中块使用青色渐变与深色文字；播放圆盘保持独立圆形轮廓与深色图标 |
| 居中的文字导航和青色下划线 | 顶部五个主标签及字幕二级标签采用一致的文字导航；文件来源使用图标侧栏 |
| 文件浏览的细分隔线、轻量列表 | 文件行取消常驻大块底色，保留分隔线、类型图标、大小信息及射线悬停高亮 |
| 投影页的双列布局 | 左侧六种投影和三种立体布局；右侧直接调节观看参数；底部保留已有识别操作 |
| 字幕样式的双列布局 | 左侧预设、圆形色样、开关和强度；右侧上下排列明亮 / 深色字幕预览 |
| 设置与信息的分组卡片 | 设置按已有功能分组；信息先展示概览，再直接展示双列诊断卡片，向下滚动即可阅读 |
| 光束定位和无分界的跳转反馈 | 细白光芯、青色光晕与渐隐尾部；上方时间标签；只有原有可跳转区域显示预览 |
| Emby 图片卡片的边缘质感 | 海报 / 横图使用圆角遮罩；Disc / Logo 保留透明通道与原有宽高比；完整演员列表仍在片段与花絮之间 |

没有复制概念图中多余的光标圆点、图钉尖角、重复功能、虚构指标或生成错误文字。概念图中没有对应真实操作的提示也未加入。原有跳转范围、主 / 快捷面板互斥、返回路径、字幕状态和播放行为继续由既有逻辑管理。

UI 保持原有世界尺寸和观看距离。视觉圆角、外部阴影和色样均不扩大交互区域；光束、标签与装饰不拦截射线。色样不叠加蓝色光照，确保显示实际选色。预览位置不修改实际播放进度。

## 渲染方式

新 Shader 与材质在 `Assets/VRPlayer/Shaders/UISurface.shader` 和 `Assets/VRPlayer/Resources/UI/Surface.mat`。每个圆角面板使用一个四顶点网格，通过 Canvas 的 TexCoord1 / TexCoord2 传递尺寸和状态；共用材质，支持 uGUI Stencil、RectMask2D 和 Unity 的立体渲染宏。沿用世界空间透明 UI 的深度检测，未增加相机、屏幕抓取、实时模糊或视频后处理。

圆角遮罩的 AlphaClip 仅按面板轮廓裁剪，阴影不会写入扩大后的遮罩。Shader 不受支持时保留原有圆角网格和默认 UI 材质作为降级路径。

Canvas 扩展通道 API 参考 [Unity 官方文档](https://docs.unity.com/en-us/engine/6000.5/script-reference/unityengine/canvas/additionalshaderchannels)；实际编译、渲染和测试均使用本项目要求的 Unity 6000.3.23f1。

## 原生渲染预览

截图比例和文字尺寸以本项目的 VR 界面为准。Emby 的此组合成样例未关联真实账户，因此显示缺少图片的占位状态，未向真实服务器请求素材。

### 文件与投影

![文件行、侧栏与主导航](Images/UIRefinement/01-Files.png)

![投影与立体布局](Images/UIRefinement/02-Projection.png)

### 字幕

![字幕选择](Images/UIRefinement/03-SubtitleSelection.png)

![双列字幕样式与明暗预览](Images/UIRefinement/04-SubtitleStyle.png)

### 设置与信息

![设置分组](Images/UIRefinement/05-Settings.png)

![播放概览](Images/UIRefinement/06-Info.png)

![同页向下滚动后的诊断](Images/UIRefinement/14-InfoDiagnostics.png)

### Emby

![媒体库卡片](Images/UIRefinement/07-EmbyWall.png)

![影片详情](Images/UIRefinement/08-EmbyDetails.png)

![片段、完整横向演员列表与花絮的顺序](Images/UIRefinement/09-Cast.png)

### 快捷控件

![渐变快捷条](Images/UIRefinement/10-Quick.png)

![定位光束与时间](Images/UIRefinement/11-QuickBeam.png)

![快捷字幕](Images/UIRefinement/12-QuickSubtitles.png)

![快捷投影](Images/UIRefinement/13-QuickMode.png)

## 验证

完整 EditMode 测试：538 / 538 通过。新增检查覆盖真实渐变渲染、圆角遮罩与阴影边界，以及换片后不显示退役播放会话的诊断值。第一轮新渲染测试因其他画布进入测试相机失败；隔离渲染层后通过，保留失败记录。

当前主工程已刷新脚本并确认编译就绪，刷新后的控制台错误 0 条；独立副本与当前主工程的完整 EditMode 均为 538 / 538 通过。主工程测试结束后仅有三个既有测试预期的 inactive 协程错误日志，与编译及设备运行错误分开记录。首轮测试、原生预览和 APK 构建使用忽略目录内的独立副本，主工程全套测试通过 Unity MCP 另行复核。没有重新运行 Configure 或更改生产场景。项目原有 `ProjectSettings.asset` 修改保持一致，SHA256 为 `ABF518CD67F358ADB71F80D2ECCC23C2326518488208E0F2F4E0F910A419EB38`。

Android Development APK 构建成功，0 错误，已安装 PICO A8110。产物 `Builds/LsVRPlayer.apk`，145497637 字节，SHA256 `76dcd80d69ab0fce4f68a8e209b45ce7a024d6aa01838256337a66638298a2e1`。最终 Runtime 文件与构建副本逐文件一致。上一版 APK 保留在忽略的验证目录供回退。

PICO 快捷控件检查 17 / 17 通过；持续观察 483 次，焦点、已知佩戴状态及新鲜诊断连续有效；播放器设置和 USB 保持唤醒设置均验证恢复。双眼截图中的面板、圆盘、渐变及弹窗正常；设备 Unity 日志未发现 Shader 编译错误。结束时一次诊断采样显示 71.8 fps、渲染倍率 1.25×；此单次观察不是性能基准或长时间流畅性结论。报告保留为 `Docs/Validation/UIRefinement/quick-controls-device.json`。

下图是本轮 PICO 双眼截图，仅含生成的 Recognition 样片及自动字幕，已检查没有个人媒体信息。截图仍不替代佩戴者的主观评价。

![PICO 上的渐变快捷条](Images/UIRefinement/15-PICOQuick.png)

PICO 返回导航检查 26 / 26 通过，覆盖物理上级与实际访问历史的区别、列表位置恢复、两手按键仲裁、追踪恢复、键盘 / 二级面板消费返回、来源切换和独立字幕入口边界。内置持续焦点 / 佩戴 / 诊断监控通过，设置和命令文件严格恢复，运行拥有的临时目录移除，`cleanupPassed=true`。报告为 `Docs/Validation/back-navigation-20261014T124317Z-fb8ec3c44f/report.json`。两轮设备测试都未修改 PICO `screen_off_timeout`。验证结束已重新打开正常播放器。

原始构建、测试与渲染日志位于忽略的 `Tools/.runtime/ui-refinement/`；设备备份、原始报告和设备截图仅保存在忽略的验证 / 截图目录。原生预览工具导出 14 张截图，另加入一张经过隐私检查的 PICO 双眼截图。

模拟输入、截图和短片性能采样不能替代实体手柄绑定、文字锐度、双眼舒适度、长时间佩戴或真实 Emby 服务器验收。

## 复现

```powershell
& $UnityEditor -batchmode -projectPath $Snapshot -buildTarget Android -runTests -testPlatform EditMode -testResults $Results -logFile $Log
& $UnityEditor -batchmode -projectPath $Snapshot -buildTarget Android -executeMethod VRPlayer.Editor.UIRedesignPreview.ExportBatch -uiPreviewOutput $CaptureOutput -quit -logFile $PreviewLog
python Tools/Device-QuickControlsTest.py --serial <PICO_SERIAL>
python Tools/Device-BackNavigationTest.py --serial <PICO_SERIAL>
```

预览工具限定独立批处理环境，不会在正在使用的编辑器场景中创建演示内容。
