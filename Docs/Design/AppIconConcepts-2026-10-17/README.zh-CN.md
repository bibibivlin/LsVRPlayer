# L's VRPlayer 应用图标备选稿

日期：2026-10-17

**已选用 A 方案。** 图标资源与 Unity 接入情况见 [A 方案应用图标](../../AppIcon.zh-CN.md)。下文保留最初的概念稿与设计依据。

本轮为四套应用启动图标概念，供选择造型；生成方式为内置 image_gen。图像尚未接入 Unity，也尚未作为 PICO 商店分层素材或 Android 自适应图标验收。

## 设计依据

- 项目现有界面：深蓝背景、海绿色强调、柔和渐变。
- [PICO 应用图标与分层设计](https://developer-cn.picoxr.com/document/spatial-design/app-icon/)：关注中心区域、缓冲区域、圆形裁切与图层拆分；正式导出时按 PICO 模板复核，背景层不透明，主体边缘清晰，并去除会与系统叠加的装饰性高光或阴影。
- [Android Adaptive icons](https://developer.android.com/develop/ui/compose/system/icon_design_adaptive)：前景与背景分层，各层 108×108 dp，核心标识安排在中央 66×66 dp 安全区；不要把系统圆角形状画死在素材里。
- 已阅读本机 imagegen 技能，采用其中的 logo-brand 工作流。另检索到 [Logo Designer](https://www.skills.sh/luongnv89/skills/logo-designer) 和 [App Icon Design](https://www.skills.sh/rebuildup/design-skills/app-icon-design) 技能作为可选资源；本轮没有安装外部技能或执行其脚本。

## 方案

| 编号 | 方向 | 侧重点 | 图片 |
| --- | --- | --- | --- |
| A | L + 播放 | 个人品牌、简洁几何与字母记忆 | [A-LPlay.png](A-LPlay.png) |
| B | 环幕影院 | 曲面屏幕与沉浸观影 | [B-ImmersiveCinema.png](B-ImmersiveCinema.png) |
| C | VR 目镜 | 直接表达 VR 视频播放器 | [C-VRVisor.png](C-VRVisor.png) |
| D | 全景视界 | 环绕轨道与全景播放 | [D-PanoramaOrbit.png](D-PanoramaOrbit.png) |

以上名称是概念方向；最终图形以图片为准。定稿阶段需要收紧主体尺寸和高光，检查 48/32 像素与圆形裁切，制作独立前景/背景及单色稿，并在设备上检查启动器中的实际效果。当前生成稿没有严格满足提示词中的中央安全区尺寸，不应直接作为发布素材。

完整生成提示词见 [Prompts.md](Prompts.md)。
