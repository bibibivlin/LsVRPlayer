# A 方案 UI 重构总览

整理日期：2026-10-16。本文描述当前实现；各阶段文档中的图片、APK 哈希和验证结果对应当时版本。

## 当前实现

| 范围 | 行为 |
| --- | --- |
| 设计语言 | 深色影院底色、海绿色强调、统一圆角、层次渐变与克制高光；解析式界面着色，不使用实时背景模糊 |
| 导航 | 五个主标签相对窗口居中；无框悬停仅改变文字颜色，下划线使用非线性动画移动 |
| 主页面 | 文件来源侧栏、投影和立体布局、字幕选择/位置/样式、播放设置与同页完整诊断；统一文字中线、卡片留白和滑条间距 |
| 操作控件 | 语义明确的操作采用统一正方形线性图标，悬停提供提示；开关缩小可见尺寸，保留整行点击和非线性位移/渐变 |
| 快捷控制 | 面板背景本身显示进度；上半区域大面积跳转，光束和放大时间跟随命中；按钮居中排列，下方独立关闭按钮保持停止并返回文件页 |
| Emby | 统一背景，自适应卡片布局，继续播放使用横向缩略图；封面等比铺满，Disc/Logo 保留透明图形；完整演员与主创横向虚拟列表位于影片片段和花絮之间 |
| SMB 与收藏 | 非交互分类标题使用文字和细线；编辑、移除使用统一图标按钮，并保留收藏撤销 |
| 运行时修正 | 初始化与行复用同步应用最终控件颜色，修复页面切换的灰白闪烁；保留原有拖动归属、取消和共享输入路径 |

未修改原生播放后端、SMB 预读、认证协议或设置迁移。现有字体资源及 GUID 保留。概念图与演示纹理仅用于设计文档和隔离预览，不作为运行时内容或真实诊断数据。

## 最新界面

![字幕样式与紧凑开关](Images/UIStyleGrid/04-SubtitleStyle.png)

![投影布局](Images/UIStyleGrid/02-Projection.png)

![SMB 分类与编辑按钮](Images/UISmbHierarchy/33-SmbGroups.png)

![Emby 首页示例](Images/EmbyUIRefinement/24-EmbyHome.png)

## 设计与实施记录

| 阶段 | 文档 |
| --- | --- |
| 多方案与 A 方案修订 | [初始概念](UIRedesignConcepts.zh-CN.md)、[A2](UIRedesignA2Review.zh-CN.md)、[A3](UIRedesignA3Review.zh-CN.md)、[光束](UIRedesignA4QuickMarker.zh-CN.md)、[渐变蒙版](UIRedesignA5QuickGradient.zh-CN.md) |
| 基础实施与材质 | [初次实施](UIRedesignImplementation.zh-CN.md)、[视觉细化](UIVisualRefinement.zh-CN.md) |
| 图标、对齐与闪烁 | [图标与导航](UIIconsAndNavigation.zh-CN.md)、[对齐](UIAlignmentFixes.zh-CN.md)、[闪烁修复](UITransitionFlickerFix.zh-CN.md)、[标签动效](UITabInteraction.zh-CN.md) |
| 卡片与 Emby | [布局间距](UILayoutSpacing.zh-CN.md)、[Emby 调整](EmbyUIRefinement.zh-CN.md) |
| 开关与滑条 | [开关动效](UISwitchMotion.zh-CN.md)、[滑条间距](UISliderSpacing.zh-CN.md)、[样式列与投影排版](UIStyleGrid.zh-CN.md) |
| 最终列表调整 | [SMB 分类与列表操作](UISmbHierarchy.zh-CN.md) |

## 验证范围

以下均为实施期间已有记录。本次整理提交沿用用户“不再测试”的要求，未追加运行编辑器或设备测试。

| 记录 | 结果与范围 |
| --- | --- |
| 最近一次完整 EditMode 套件 | 滑条阶段 555 / 555；不代表之后新增改动已重跑完整套件 |
| 最终 SMB 改动后的相关套件 | EditMode 162 / 162，PlayMode 9 / 9；含分类标题与行复用回归 |
| 原生界面 | 最终隔离导出 35 张，重点检查最新样式、投影、SMB 与收藏排版 |
| PICO 历史阶段 | 基础快捷控制 17 / 17、返回导航 26 / 26；Emby 合成服务 4 / 4；开关 5 / 5；滑条 8 / 8；样式与投影 7 / 7。各版本的连续佩戴/焦点、恢复及限制详见对应报告 |
| 最终 SMB 设备回归 | 未完成；USB 连接不可用后按用户要求停止，不借用历史结果作为本版验收 |
| 最终 APK | Android ARM64 / IL2CPP 开发版构建成功，145,521,428 字节；哈希与产物信息见 SMB 记录 |

设备自动化使用开发模拟输入及短测试片段；静态预览、真实局域网、合成服务和物理手柄/双眼舒适度验收分别记录，不相互替代。原始设备记录、设置与凭据备份、临时脚本和 APK 保持忽略；文档配图经检查，使用合成资料或生成测试片段。

## 从源码复现

按 [构建说明](BuildAndUse.zh-CN.md) 使用 Unity 6000.3.23f1 打开正式场景并构建 APK。回归程序集为 `VRPlayer.Tests`（EditMode）和 `VRPlayer.PlayModeTests`（PlayMode）。界面预览入口为 `VRPlayer.Editor.UIRedesignPreview.ExportBatch`，仅允许在隔离批处理工程运行；通过 `-uiPreviewOutput <目录>` 指定输出位置，`-uiArtworkFixtures` 可填充合成封面。不得在生产场景注入演示对象。
