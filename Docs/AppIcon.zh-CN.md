# A 方案应用图标

日期：2026-10-17。用户选定 A「L + 播放」，已接入 Unity 项目。

![应用图标预览](Images/AppIcon-A-Preview.png)

## 图形与资源

保留 L 字母、折叠的播放箭头、海绿色渐变与深蓝底色。兼容图标直接使用已选定的概念图；自适应前景保留同一造型，并调整透明留白，以适应系统裁切。

| 文件 | 用途 | 源尺寸 |
| --- | --- | --- |
| [AppIcon.png](../Assets/VRPlayer/Art/AppIcon/AppIcon.png) | 默认、Legacy 和 Round 兼容图标；与原 A 稿字节一致 | 1254×1254 |
| [AppIconBackground.png](../Assets/VRPlayer/Art/AppIcon/AppIconBackground.png) | Android 自适应图标不透明纯色背景 #0A1A23 | 1024×1024 |
| [AppIconForeground.png](../Assets/VRPlayer/Art/AppIcon/AppIconForeground.png) | Android 自适应图标透明前景 | 1254×1254 |

图标资源位于 Art 而非 Resources，不由播放逻辑加载。资源附有 Unity 生成的 .meta，采用 sRGB、无 Mipmap、无压缩与 Clamp 导入。已有图片 Git LFS 规则覆盖这些 PNG。

生成方式为内置 image_gen；[完整提示词](Design/AppIcon-A-Prompts.md)已保留。原始四套概念仍在 [备选稿目录](Design/AppIconConcepts-2026-10-17/README.zh-CN.md)。

## 接入

[PlayerIcons.Configure](../Assets/VRPlayer/Editor/PlayerIcons.cs) 配置默认图标，并填充 Android 模块暴露的所有图标槽位。自适应图标按照 Unity 6000.3 的背景、前景顺序配置；系统负责最终形状裁切。Unity 的图标 API 要求使用该目标与类型对应的槽位数组，本次通过 GetPlatformIcons 取得并回读验证。[Unity 官方 API](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/PlayerSettings.SetPlatformIcons.html)

菜单 **VRPlayer → Apply App Icon** 可单独重新应用，不打开或重建场景。现有 **Configure Project and Open Player** 与 **Build Android APK** 流程也会应用图标。

## 验证与边界

- Unity **6000.3.23f1** 完成批处理编译及配置：6 个 Adaptive、6 个 Round、6 个 Legacy 槽位引用验证通过，另设置默认图标。
- 前景含真实 Alpha。以 Alpha ≥ 16 的像素测量，主体包围框为 x=396–921、y=336–953，最远像素半径为画布宽的 **0.291986**，位于 Android 中央 66/108 安全圆半径 **0.305556** 内。[Android 官方规范](https://developer.android.com/develop/ui/compose/system/icon_design_adaptive)
- [静态预览页面](Design/AppIcon-Preview.html)及上述截图检查了圆角、圆形和自适应合成，以及 32 / 48 / 64 CSS 像素显示；主体没有被裁掉，L 与播放箭头仍可辨认。
- 原始日志与像素测量保留在忽略的 Tools/.runtime/app-icon/。本次未运行播放相关回归，未修改播放行为。

本轮未重新生成或安装 APK，尚未验证 PICO 实际启动器显示。该预览不替代实机验收；也不是 PICO OS 6 空间桌面专用的上架分层包。

复现配置检查（仓库根目录）：

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe' -batchmode -nographics -quit -projectPath (Get-Location).Path -executeMethod VRPlayer.Editor.PlayerIcons.Configure -logFile 'Tools/.runtime/app-icon/unity-configure.log'
```
