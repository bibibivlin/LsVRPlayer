# 多语言界面与验证

## 使用

打开 **设置 → 界面语言**，选择 **跟随系统 / 简体中文 / English / 日本語**。选择后立即更新界面并保存，重新启动后继续使用该选项。跟随系统支持简体/繁体中文系统、英语和日语；中文系统使用当前简体中文界面，其他尚未支持的系统语言回退到英语。应用重新获得焦点时会重新检查系统语言。

主面板、快捷面板、文件来源、SMB 配置、Emby 浏览、字幕设置、播放信息和应用状态提示使用所选语言。文件名、目录名、服务器/账户名称、影片简介、演员资料、音轨名称和字幕内容保留原文。

隐私确认、权限说明和开源许可中的应用说明继续保留中英双语。中文界面中文在前；英语和日语界面英文在前。隐私政策和权限全文仍可切换中英版本，语言按钮按相同顺序排列并默认选中优先版本。开源组件清单和各许可证英文正文逐段保持原样，未新增日文法律文本，也未更改条款版本或已有确认。

## 维护

- `Assets/VRPlayer/Resources/Localization/Strings.json` 保存中文源文案及英语、日语译文。新增界面文案使用 `Localization.T`，带数值等参数的文案使用 `Localization.F`，保留一致的参数编号和格式。
- `Localization.Message` 只用于应用生成的状态和已知后台错误，按完整文案或显式模板匹配；文件名、地址等参数保持原样。只有声明 `localizeArguments` 的错误模板会继续翻译内层错误。未知后台原始细节保留原文。
- 导航标识、媒体 URI、缓存证据和诊断文件继续使用原有值。`UserSettings.language` 是独立枚举字段，缺失或无效时使用跟随系统；现有设置版本仍为 3，保留 v1 → v2 → v3 迁移。
- 沿用现有 Noto Sans CJK 字体源和 `UIChinese.asset` GUID。构建的字体预填充同时读取语言资源，动态字体继续处理媒体名称与字幕。未增加运行时依赖。

## 2026-10-18 验证

Unity **6000.3.23f1**，Windows 编辑器：

- 完整 `VRPlayer.Tests` EditMode：**628 / 628 通过**（含 5 项开发版页面文字采集回归和 3 项快捷字幕空列表提示宽度回归）。
- 最后界面与法律页调整后的本地化、主/快捷面板和 UI 回归：**188 / 188 通过**。原始结果保存在忽略目录 `Docs/Validation/Localization/`。
- 验证语言匹配、显式覆盖、语言持久化、旧设置迁移、非法枚举回退、原文文件名、目录滚动位置、快捷弹层、条款顺序及英文许可证正文保持原样。
- 语言资源检查覆盖全部翻译的非空值、重复键和格式参数。使用随附字体源检查全部英语、日语译文字形。
- 三语各导出 44 张原生 uGUI 合成界面截图，覆盖文件、投影、字幕、设置、信息、Emby、快捷面板、键盘及法律页面。英日固定界面文案检查无省略截断；较长合成影片标题继续使用原有省略显示。中文 CD 卡片沿用原有单行进度摘要，长摘要仍可能省略；英日摘要支持两行。精选截图仅含合成媒体信息和应用公开条款。

截图导出在独立验证项目副本中运行，避免占用正在编辑的项目：

```powershell
& '<Unity 6000.3.23f1>/Editor/Unity.exe' -batchmode `
  -projectPath '<验证项目副本绝对路径>' `
  -executeMethod VRPlayer.Editor.UIRedesignPreview.ExportBatch `
  -uiPreviewLanguage Japanese `
  -uiPreviewOutput '<截图输出绝对路径>' `
  -logFile '<日志绝对路径>' -quit
```

`-uiPreviewLanguage` 可替换为 `Chinese` 或 `English`。截图旁的 `*-alignment.json` 包含文字尺寸及截断标记。

PICO 实机三语页面、设置保存、播放及法律顺序检查已完成，用户确认英日佩戴可读性与真实手柄操作。实机发现的日语快捷字幕空列表提示截断已修复。构建标识、独立物理复核、恢复记录和验证边界见 [PICO 多语言实机验收](LocalizationDeviceValidation.zh-CN.md)。

## 精选截图

![英语设置](Images/Localization/Settings-English.png)

![日语设置](Images/Localization/Settings-Japanese.png)

![语言选择](Images/Localization/Language-English.png)

![英语优先的双语隐私确认](Images/Localization/Consent-English.png)

![日语界面中的英文优先许可说明](Images/Localization/Licenses-Japanese.png)
