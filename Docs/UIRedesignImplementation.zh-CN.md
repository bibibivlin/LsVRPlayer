# A 方案 UI 实施与验证

日期：2026-10-13。实现以 A 方案及 A3、A4、A5 修订为依据；下方为 Unity 原生 uGUI 渲染截图。截图使用明确的演示数据，无真实媒体、服务器、头像或凭据。它们验证排版与组件绘制，不代表真实影片解码数据、双眼显示或佩戴舒适度验收。

## 已实现

- 统一深色影院底色、海绿色强调、圆角卡片、文字层级、悬停/按下/禁用反馈与几何图标。沿用原中文字体和 GUID；运行时不使用生成海报、概念图、伪造图标或演示指标。
- 文件页使用独立来源侧栏；其他页面保留完整宽度。目录行与字幕候选行复用可见卡片，保留查询、排序、访问历史和位置恢复。修复布局刷新中滚动条的旧归一化位置反写内容的问题。
- 快捷条无额外标题和海报，背景全高显示真实播放进度。上一部 / 播放暂停 / 下一部对称居中，下方独立 X 仍停止并返回文件页。原上方 110/240 单位的跳转区域保持不变；下方按钮和空白区域不会触发跳转。
- 上下区域用宽缓的渐变蒙版融合，无水平分割线。预览为细亮芯与柔和光晕的光束，跟随射线命中的横纵位置；不额外绘制圆点或图钉。时间字体 34，边缘限制在面板宽度内。装饰图形不接收射线；拖动预览不替代实际播放进度。打开弹窗、失焦及已有输入取消路径取消操作而不提交。
- 投影与立体布局分组，平面距离和全景缩放按当前投影显示。保留六种投影、所有立体与手动锁定选项、识别/撤销、目录偏好和既有 VR180 识别门控。
- 字幕保留选择 / 位置 / 样式页；候选列表完整可滚动，样式同时在亮、暗背景预览，不增加位置跳转提示。快捷字幕保留原有候选、开关和操作，不增加自动/手动模式单选框。
- Emby 七种视图按侧栏后剩余宽度重排，不缩放窗口来挤入内容。保留图片比例及 Disc/Logo 透明通道、视图记忆、账户隔离和原始播放。
- 影片片段 → 演员与主创 → 花絮与预告。移除演员 24 人截断；完整名单横向滚动、头像按可见卡片加载，卡片池为可见数量加缓冲。指向演员条时水平摇杆滚动演员，沿用原轴仲裁和回中要求；人物页入口保持。
- 设置分组，信息页同页滚动显示解码、缓冲、SMB、渲染与应用诊断，无折叠诊断入口；数值来自真实快照，无有效 SMB 样本时明确说明。

## VR 尺寸与输入

沿用完整面板 1440 × 1040、快捷画布 1440 × 1120、统一缩放 0.00145 和原观看位置。完整面板约 2.09 × 1.51 米，在 2 米距离的角宽约 55°；没有为了容纳内容扩大窗口或强制持续跟随头部。尺寸换算依据 [Unity 世界空间 Canvas](https://docs.unity3d.com/Packages/com.unity.ugui@2.0/manual/HOWTO-UIWorldSpace.html)。

主要新控件使用约 58–62 画布单位高的命中区域，列表行距 80。字号与文字框高度结合现有 Noto Sans CJK 的实际行高校验，避免短框裁掉中文。几何圆角、渐变和光束使用默认 uGUI 材质与少量顶点，不引入实时模糊或背景截帧。

射线仍使用当前有效 Canvas，保留原手柄圆点、开启扳机消耗、双指针拖动归属、焦点/追踪中断取消和水平/垂直轴仲裁。Y/B/Esc 仍走共享返回路径，物理上级独立。未改变原生解码器、网络预读、格式枚举、持久化迁移或认证方式。

## 实际界面

### 文件

![文件浏览](Images/UIImplementation/01-Files.png)

### 投影

![投影设置](Images/UIImplementation/02-Projection.png)

### 字幕样式

![明暗字幕样式预览](Images/UIImplementation/04-SubtitleStyle.png)

### 设置与信息

![设置](Images/UIImplementation/05-Settings.png)

![直接展示的诊断](Images/UIImplementation/06-Info.png)

### Emby

![七视图中的海报视图](Images/UIImplementation/07-EmbyWall.png)

![影片详情](Images/UIImplementation/08-EmbyDetails.png)

![完整横向演员列表](Images/UIImplementation/09-Cast.png)

### 快捷条

![快捷条](Images/UIImplementation/10-Quick.png)

![光束和渐变](Images/UIImplementation/11-QuickBeam.png)

![快捷字幕](Images/UIImplementation/12-QuickSubtitles.png)

2026-10-14 继续细化材质、控件层次和布局，最新预览及验证见 [A 方案视觉细化](UIVisualRefinement.zh-CN.md)。本页保留第一版结果。

## 验证记录

Unity 6000.3.23f1，完整 EditMode 536 / 536 通过，0 失败、0 跳过。包括既有播放、字幕、格式识别、导航、Emby 和渲染回归，以及新加入的命中范围、装饰射线、预览不修改进度、弹窗取消、完整演员池、七视图几何、同页诊断、隐藏页面滚动和模态退出检查。旧测试中的密度/宽度断言改为对应的实际行距和视口，保留行为断言。

Android Development APK 构建成功，0 错误。产物：`Builds/LsVRPlayer.apk`，116935116 字节，SHA256 `b32db0c79765e45753465d2e0da58d89891207b1fa14112a273b8b76bbdc4893`。工程已有 `ProjectSettings.asset` 修改未改变；其 SHA256 始终为 `ABF518CD67F358ADB71F80D2ECCC23C2326518488208E0F2F4E0F910A419EB38`。

批量导入、自动测试、预览及构建在忽略的独立工程副本执行，避免打断当前编辑器。随后通过恢复的 Unity 编辑器连接刷新脚本并确认当前工程编译就绪、控制台错误 0 条；未进入 Play Mode 或改动场景。最终 Runtime 文件与副本逐文件核对一致。原安装 APK 和原构建 APK 在忽略目录保留。

原始测试、构建与导出记录：`Tools/.runtime/ui-redesign-work/`。设备记录、备份与截图：`Docs/Validation/`、`Docs/Captures/`，均不进入版本控制。

PICO A8110 上安装并测试以上 APK：

| 检查 | 结果 | 连续性与恢复 |
| --- | --- | --- |
| 快捷面板、字幕、跳转、倍速、距离/缩放、互斥显示与隐藏 | 17 / 17 通过 | 462 次焦点、已知佩戴状态、诊断帧/时间戳观察；无中断。设置文件字节恢复及 USB 保持唤醒设置恢复均通过 |
| 全局 Y/B 返回、访问历史、物理上级、查询与滚动位置、主/快捷字幕边界、键盘/二级面板消费返回 | 26 / 26 通过 | 内置连续焦点/佩戴/诊断监控通过；设置和命令文件严格恢复，运行拥有的临时目录移除，cleanupPassed=true |

为满足持续有效状态要求，快捷测试工具增加了与返回导航工具同类的焦点/佩戴/诊断新鲜度监控，中断使运行失败并恢复设置。未修改 PICO screen_off_timeout。第一版原始报告保留为 `Docs/Validation/UIRedesign/quick-controls-device.json` 和 `Docs/Validation/back-navigation-20261013T145009Z-7725955a88/report.json`。自动操作结束后已重新打开正常播放器。

这些模拟手柄输入通过共享 `VrUiInput` 路径，并且运行时真实头显保持佩戴；仍不能替代实体按键绑定、射线手感、文字锐度、双眼舒适度和长时间佩戴的主观验收。未使用真实 Emby 账户开展服务器端回归。

## 复现

在 Unity Test Runner 的 EditMode 中运行 `VRPlayer.Tests`。批量验证请使用独立工程副本，复制当前 `Assets/`、`Packages/`、`ProjectSettings/`；可复制 `Library/PackageCache/` 缩短导入，不共享或硬链接可写资源。

```powershell
& $UnityEditor -batchmode -projectPath $Snapshot -buildTarget Android -runTests -testPlatform EditMode -testResults $Results -logFile $Log
& $UnityEditor -batchmode -projectPath $Snapshot -buildTarget Android -executeMethod VRPlayer.Editor.UIRedesignPreview.ExportBatch -uiPreviewOutput $CaptureOutput -quit -logFile $PreviewLog
python Tools/Device-QuickControlsTest.py --serial <PICO_SERIAL>
python Tools/Device-BackNavigationTest.py --serial <PICO_SERIAL>
```

预览工具限定批处理环境，以合成元数据实例化真实组件，拒绝在正在使用的编辑器场景中生成演示 UI。APK 按既有构建流程生成；本次验证副本直接构建现有 `Player.unity`，未重新运行修改工程设置的 Configure。
