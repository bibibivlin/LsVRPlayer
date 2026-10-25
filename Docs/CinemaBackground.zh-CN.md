# 平面观影背景

设置 → **观影背景** 提供五种静态背景，选择立即生效，跨影片、重启保留。新安装和旧版设置迁移默认使用纯黑。第二轮新增哑光幕墙与独立亮源控制，见 [平面观影舒适调节](CinemaComfort.zh-CN.md)。

2026-10-24 用户试用反馈：光晕仍随画面/头部运动，背景也可能增加干扰。下文记录第一版的功能与验证，不代表减眩效果已通过主观验收。后续方向见 [第二轮研究与设计建议](CinemaBackgroundResearch.zh-CN.md)。

| 背景 | 表现与用途 |
| --- | --- |
| 纯黑 | 清屏为真正的 RGB 0，不绘制背景，作为对比基准。 |
| 中性暗灰 | 均匀中性灰，单独比较背景亮度的影响。 |
| 屏幕柔光 | 屏幕外侧宽阔、连续的亮度过渡，远处保持纯黑；不出现亮色边框。 |
| 暗色影院 | 低对比度的哑光墙面、墙角、顶面和地面，加上局部柔光，提供静止空间参照。 |
| 哑光幕墙 | 固定墙/地面明暗，没有屏幕亮圈、周期条纹或倒影；不受柔光范围影响。 |

建议先尝试「屏幕柔光」，背景亮度 **16 / 255**，柔光范围 **12°**；再根据头显与影片比较纯黑、8、16、24、32 等灰阶。所有参数是待实机调优的工程起点，不是已被研究确证的最优值。

- 亮度范围 0–48 / 255，表示 SDR/sRGB 灰阶代码值，**不是物理亮度百分比**。例如 #101010 对应约 0.52% 的线性参考白；应用不能据此推断头显的 cd/m²。
- 柔光范围 5°–25°，用于屏幕柔光和暗色影院。按当前屏幕距离、宽度、宽高比、高度、曲率计算角度包络，边缘再柔和衰减。
- 「收起面板预览」可隐藏明亮的设置面板观察环境。背景是全局偏好；影片亮度仍在设置首页单独调节。
- 所有非平面投影自动停用背景，包括 VR180 背面的区域。返回平面时恢复之前的选择，不改影片格式锁、字幕、距离或全景缩放。
- 缩略图使用较亮的 48 / 255 便于识别布局，不代表当前实际亮度。

![背景设置](Images/CinemaBackground/Settings.png)

![暗色影院合成画面示意，灰阶24/255，不模拟透镜光晕](Images/CinemaBackground/QuietTheater.png)

## 设计依据与边界

采用用户提供的《VR 观影时，如何通过背景设计减轻镜片光晕？》中优先比较静态背景的建议。背景只能尝试降低光晕的主观显著性，无法消除镜片杂散光、鬼影或反射；增加背景亮度也可能增加杂散光。因此保留纯黑，并限制背景亮度。

[ITU-R BT.2035](https://www.itu.int/rec/R-REC-BT.2035-0-201307-I) 是传统 HDTV 参考观看环境标准，不能直接当作 VR 参数；[Bigscreen Creator Club](https://github.com/BigscreenVR/bigscreen-creator-club) 的影院环境资料提供空间环境设计参考。[YouTube Ambient Mode 官方设计说明](https://blog.youtube/inside-youtube/youtube-ambient-color-mode-visual-language-redesign/) 描述内容颜色驱动的环境效果，但不证明其能改善 VR 镜片光晕。

本次实现五种静态方案。内容感知动态环境光留作独立后续实验：需要单独的 GPU 降采样、低饱和度与亮度上限、数秒时间平滑、闪光抑制、播放代际和取消控制。本实现不会采样视频或随影片闪光变化。

## 实现

- `CinemaBackgroundSettings` 在现有 v3 全局设置中新增可选对象。缺失、null、非法枚举和非有限值均归一化；保留 v1 → v2 → v3 迁移。
- `PlayerApp` 接收选择与调节；选项立即保存，滑块释放或离开页面时保存，不在拖动每帧写文件。
- `VideoPresenter` 拥有背景资源。背景方向沿播放器初始化的稳定朝向，不跟随头部转动；影院空间原点保持在初始观看位置。影片继续沿用现有跟随头部平移的屏幕定位行为。
- 一个 12 三角形包围盒、一个无光照材质；无纹理资源、光源、碰撞体、视频回读或解码器改动。球面方向上的平滑角度包络覆盖曲面屏。影院使用程序化墙面交点与宽缓明暗变化，不增加细碎纹理。
- Shader 在背景队列绘制、不写深度，影片和世界空间菜单随后绘制。包含立体实例化宏，并按项目的 Gamma/Linear 路径转换灰阶；构建配置显式保留 Shader。
- 对象销毁时释放材质/网格并恢复原相机清屏设置。设置页、三种语言与模态 Back 使用原有 UI 路径。

## 验证与复现

Unity **6000.3.23f1**，EditMode：`CinemaBackgroundSettingsTests`、`CinemaBackgroundRenderingTests`、`LocalizedPlayerUITests`，并检查原有测试套件。新增覆盖旧设置迁移、重启、非法数值、真实渲染灰阶、屏幕外衰减、头部旋转、影片遮挡、全景停用/恢复、三种语言的选择与 Back 保存。

Unity 菜单 **VRPlayer → Validation → Capture Cinema Backgrounds** 生成合成画面与三种语言的设置截图到忽略目录 `Docs/Captures/CinemaBackground/`，同时输出 `truncated-labels.txt`。截图不能验证透镜光晕。

设备回归（只对明确指定的 PICO 执行）：

```powershell
python Tools/Device-BackgroundTest.py --serial <PICO_SERIAL> --apk Builds/BackgroundValidation.apk
```

若正式版与开发包签名不同，不卸载正式版。可临时用包名 `com.linxyi.LsVRPlayer.backgroundtest` 构建 `Builds/BackgroundIsolated.apk`，随后恢复项目的原包名/名称，再执行：

```powershell
python Tools/Device-BackgroundTest.py --serial <PICO_SERIAL> --package com.linxyi.LsVRPlayer.backgroundtest --apk Builds/BackgroundIsolated.apk
```

脚本默认拒绝覆盖已存在的隔离测试应用；只在新建测试应用中写入合成同意记录，测试结束卸载该应用。若需保留已安装的隔离应用，用 `--reuse-installed`，并在执行前单独更新 APK。合成素材放在应用自己的外部文件目录，不需授予公共存储读取权限；测试不访问正式版的账号或媒体数据。

脚本依赖 Pillow 和 imageio-ffmpeg（可放入 `Tools/.runtime/`），自动生成 12 秒暗场移动白块/文字合成片段。默认检查原四背景选择、实际背景可见性、隐藏面板截图、两个滑块、全局 Back、磁盘保存、全部非平面投影停用及回到平面恢复、停止后的背景；加 `--comfort` 覆盖哑光幕墙与亮源控制，见舒适调节文档。它要求有效头显佩戴状态、焦点和新鲜诊断，中断即失败。不会更改 `screen_off_timeout` 或 USB 唤醒设置。

开始前 force-stop 并备份原设置与私有命令文件（含原本不存在），结束后 force-stop，逐字节恢复并核验，只清理有本次运行所有权记录的设备媒体/暂存路径。清理失败则整次失败。复用模式保留应用，新建隔离包模式卸载本次安装的应用；私有备份、失败记录和设备截图保留在忽略目录。

必须另做戴机 A/B：同一影片/亮度/姿态比较暗场字幕、黑底亮物体移动、夜晚点光源、突然闪光、普通亮场；分别记录光晕显眼程度、转头干扰和画质偏好。自动化不验证物理控制器、双眼舒适度，也不能得出光晕改善比例或长片性能结论。

## 2026-10-24 验证记录

- EditMode 完整套件 **759/759 通过**；相关的设置、渲染和多语言 UI 复测 **37/37 通过**。
- 中、英、日设置页和背景页离屏截图检查完成，最终 `truncated-labels.txt` 为空。修复了缩略图渲染队列及日语说明高度。
- 普通开发 APK、隔离包均构建成功，新增 Shader 没有编译错误。构建仍有项目已有的 PICO appID、Pipeline 配置、手部 prefab/Shader 等警告；测试 Console 有现有 Quick controls inactive fixture 的协程日志，不属于背景编译错误。
- 指定的 PICO A8110 上，隔离包完整运行 **7 组检查通过**；66 次后台采样均维持有效佩戴检测、焦点及新鲜诊断。四背景、两个滑块和 Back 保存、全部全景模式停用/平面恢复、停止后的背景均通过。
- 正式版与开发包签名不同，覆盖安装失败的记录已保留，原正式版设置逐字节恢复。改用临时隔离包完成验证；原包名和产品名已在项目中恢复。
- 一次启动时佩戴/焦点不足的失败运行已保留并完成清理；最终成绩仅来自重新开始的完整运行。未合并中断片段。
- 成功运行恢复了测试应用设置/命令文件的原始缺失状态，移除了本次拥有的媒体与暂存文件，并卸载临时测试应用；未更改系统休眠或 USB 唤醒设置。
- 原始记录：忽略目录 `Docs/Validation/background-20261024T044232Z-1d10e1d9/result.json`；截图位于同名 `Docs/Captures/` 子目录。以下两张仅含合成片段与设置页，已检查不含账号、媒体路径等私人内容。设备截图包含显示系统的处理，未做光度计标定，不是透镜实拍。

![PICO 屏幕柔光，合成暗场片段](Images/CinemaBackground/PICO-SoftSurround.png)

![PICO 背景选择界面](Images/CinemaBackground/PICO-Settings.png)
