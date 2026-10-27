# Emby 媒体库排序

2026-10-28 更新菜单布局；使用 Unity 6000.3.23f1，Android 目标。

媒体库第一级提供标题、加入日期和发行日期，每项可按递增或递减排列，默认标题递增。菜单中标题方向显示为“标题正序／标题倒序”，日期方向显示为“较早在前／较晚在前”；入口只显示当前字段。菜单保留原有面板风格，通过留白减轻视觉层次；未选中项无背景和边框，射线指向时显示细边框，选中项使用重点色，点击后立即生效并收起。视图菜单以双列图标展示七种布局，图片大小单独分组。排序字段与方向按账户配置 ID、媒体库根 ID 保存在本机，并与视图和图片大小相互独立。切换排序会保留搜索条件并重新加载；返回旧的媒体库页面时，如果本库排序已改变，会按当前选择重新加载该页面。不同排序、视图和图片大小分别记住滚动位置。

请求使用 Emby 的 `SortName`、`DateCreated` 或 `PremiereDate` 及 `Ascending`／`Descending`，完整聚合分页并保留服务器顺序。下级目录、季／集和跨库分类不显示排序菜单；季／集继续按官方接口返回的顺序显示。菜单展开时阻止背景滚动，返回或点击菜单外关闭；过期菜单不能改写其他页面或账户。

## 2026-10-28 菜单更新验证

- 在独立工程副本运行最终完整 EditMode：**825 / 825 通过**，失败与跳过为 0；包括排序立即应用、字段与方向文案、双列视图、菜单射线命中、双指针悬停、菜单返回、过期回调和偏好隔离。副本使用工作区当时的字体文件，工作区中已有的字体改动未被覆盖。
- 为确认更改不依赖工作区中未提交的字体内容，将隔离副本中的字体替换为仓库原版后再次运行完整 EditMode，结果同为 **825 / 825**；两张中文悬停截图与工作区字体生成的版本逐字节一致。相关结果保存在 `clean-font-editmode-results.xml` 和对应日志中。
- 间距调整期间的一次全量运行中，视距输入的焦点中断测试未通过，失败原因未确定；随后该测试所在的 69 项单独复跑通过，后续两次全量运行为 **824 / 824**，最终悬停样式为 **825 / 825**。失败记录保留在忽略目录中的 `refined-editmode-results.xml` 和对应日志。
- 使用真实 uGUI 和合成影片数据导出中文、英文、日文的发行日期排序和海报视图菜单，分别检查默认与模拟悬停状态。菜单及入口文字未发现截断；结果和日志在忽略目录 `Docs/Captures/EmbyMenuRedesign-20261028/` 与 `Docs/Validation/EmbyMenuRedesign-20261028/`。
- 在隔离副本将排序字段切换为标题，另外检查英文与日文方向文案。英文缩短为 Ascending／Descending 后使用 26 号字号，两个语言的方向文字均未截断。
- 开发版 APK 已在隔离副本构建并复制至忽略的 `Builds/LsVRPlayer-EmbyMenus-development.apk`；包名 `com.linxyi.LsVRPlayer`、版本 `1.2.1`、架构 `arm64-v8a`，标记为可调试，APK v2 签名校验通过。SHA-256：`BDDAAEADA8C4C98FE2A97ACE8CB1FDAB2F1E9CF9CA3492938EACEEA945D4E226`。
- 以下截图不含真实账户、服务器或媒体资料。未在 PICO 头显上进行实体手柄、佩戴可读性或舒适度验收，也未操作设备设置或 Emby 服务端数据。

![更新后的排序菜单，模拟射线停在加入日期](Images/EmbyLibrarySortMenuRedesign.png)

![更新后的视图菜单，模拟射线停在横幅图](Images/EmbyLibraryViewMenuRedesign.png)

在隔离工程副本导出菜单截图：

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe' `
  -batchmode -force-d3d11 -projectPath $Snapshot -buildTarget Android `
  -executeMethod VRPlayer.Editor.UIRedesignPreview.ExportBatch `
  -uiMenuOnly -uiArtworkFixtures -uiPreviewLanguage Chinese `
  -uiPreviewOutput $CaptureOutput -quit -logFile $PreviewLog
```

## 2026-10-27 原始排序功能验证

- 隔离工程副本的最终 EditMode 全套：**822 / 822 通过**，失败与跳过为 0。该轮从仓库版本的字体资源开始，不包含工作区原有的字体改动；此前界面自适应调整后的相关测试为 **152 / 152 通过**。最新编译和重跑日志未发现 C# 编译错误或异常。
- 回归使用回环合成服务，覆盖电影与剧集库的三种字段、两种方向和 205 项跨页结果；还覆盖旧设置默认值、无效字段、账户／媒体库隔离、视图与图片大小互不覆盖、搜索、返回旧页面、下级目录与分类边界、失效菜单、旧请求取消。服务按请求头鉴权，测试不使用真实账户。
- 使用真实 uGUI 组件和合成数据在隔离工程生成中／英／日预览，人工检查排序选择器、菜单和方向按钮，三种语言的菜单文字均未截断。下图不包含服务器地址、账户或真实媒体资料。
- 首次导入副本时 Unity ShaderGraph 包的 `MaterialPostprocessor` 抛出一次空引用异常；随后编译和测试完成，后续测试及三次预览日志未出现该异常。原始结果保存在忽略目录 `Docs/Validation/EmbyLibrarySort-20261027/`。
- 发布工具拒绝门槛测试 **8 / 8 通过**；现有构建和发布 PowerShell 脚本语法检查及 `git diff --check` 通过。

![2026-10-27 的旧版排序菜单](Images/EmbyLibrarySort.png)

这些检查不代表真实 Emby 服务器或 PICO 头显验收；真实网络、物理手柄、佩戴可读性和舒适度仍需单独验证。测试未操作设备设置或服务端用户数据。

复现：Unity Test Runner → EditMode → `VRPlayer.Tests`；或在独立工程副本运行：

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe' `
  -batchmode -force-d3d11 -projectPath $Snapshot -buildTarget Android `
  -runTests -testPlatform EditMode -assemblyNames VRPlayer.Tests `
  -testResults $Results -logFile $Log
```
