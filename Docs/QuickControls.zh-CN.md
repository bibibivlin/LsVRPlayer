# 简易播放控制面板

## 使用

完整面板关闭时，对空白处按扳机开关简易控制条；对控制条、按钮或弹窗按扳机执行对应操作。呼出面板的按压会被消费，必须松开后才能点击控件。A／X／菜单键和键盘 Tab 继续开关完整面板。

控制条背景表示进度，上半部悬停显示目标时间，点击跳转，拖动后松开提交一次。下方按钮为字幕、音量、上一部、播放／暂停、下一部、模式和倍速。打开时对齐当前视线，在前方 2 米处固定；控制条中心从原位置朝视线正前方抬高三分之一，减少手柄移动幅度。指针停留或拖动期间保持显示。启用自动隐藏后，播放且离开面板 5 秒隐藏，暂停时保留。

- 音量图标悬停展开滑条；在图标、连接区域和滑条之间移动保持展开，所有指针离开后关闭。点击图标静音／恢复最近的非零播放器音量。音量与完整面板同步，不调节系统媒体音量。
- 字幕页提供开关、同名 SRT、内嵌文本字幕、独立文件浏览和 ±0.5 秒偏移／归零。正值表示字幕延后。浏览支持影片目录、上级、本地存储、系统已授权目录、收藏和已配置 SMB，不替换主文件来源或播放列表。手动选择与 Off 状态逐片保存；再次开启优先恢复上次选择。
- 模式页提供 Flat、VR180、VR360、鱼眼、Cubemap 3×2、EAC 3×2 和 Mono／SBS／OU。手动选择仍由 PlayerApp.ChangeFormat 锁定该片。平面／曲面距离为 1.2–10 米，全景缩放为 0.5–2×，默认 1×；“重置远近”仅恢复当前平面的 3.5 米距离或全景的 1× 缩放，保留投影、立体布局和格式锁定。缩放中心跟随当前头部朝向，双眼共享中心和倍率，字幕／菜单不缩放。VR180／鱼眼拉远时可能看到源图像覆盖之外的黑边。
- 倍速页使用速度表图标，提供 0.75／1／1.25／1.5／2× 快捷速度和 0.25–3× 细调，重置为 1×。简易面板不包含音轨、单眼 2D 效果开关、瞳距或画质调整。

完整和简易面板均隐藏且有影片时，上推摇杆拉近平面／曲面或放大全景，下推反向调节；暂停时也可调节。满推每秒调节 2 米或 0.5×，范围沿用模式页。双手柄不叠加倍率，横向仍为快进／快退。面板显示时上下摇杆用于列表滚动。松杆、失焦、追踪中断或显示面板时结束调节并保存，持续调节沿用周期保存；切换界面后需回中再调节。

上一部／下一部和自动连播保留完整／简易／隐藏状态，换片会关闭失效的简易弹窗。显式相邻播放仍只继承现有格式字段；距离和缩放使用目标影片记录。设置版本维持 3，原 v1→v2→v3 迁移保留，新字段缺失时使用默认值。

## 复现验证

Unity 6000.3.23f1：Test Runner → EditMode → VRPlayer.Tests。QuickControlsTests 检查显示状态、静音／字幕恢复、输入按压消费、拖动取消、双指针和保存兼容；ViewDistanceInputTests 检查摇杆方向、范围、松杆保存、界面屏蔽、失焦与追踪取消及双手柄互斥；PanoramaZoomRenderingTests 用实际投影 Shader 渲染六种投影 × 三种立体布局 × 三档缩放 × 两眼，并检查转头中心与 1× 恒等行为。

构建 Android Development APK 后，安装到 PICO。设备需有 Generate-RecognitionMedia.py --clips 生成的 Recognition 视频和 SRT。

~~~powershell
python Tools/Device-QuickControlsTest.py --serial <PICO_SERIAL>
python Tools/Device-ImprovementsTest.py --serial <PICO_SERIAL>
python Tools/Device-AdjacentPlaybackTest.py --serial <PICO_SERIAL>
~~~

头显需持续保持佩戴感应有效并让播放器处于前台。简易面板脚本可加 --section panels 或 --section distance，分别运行 11 项面板检查和 7 项远近检查（全景重置重复一项），默认 all 为 17 项；分段报告单独保存。输入在失焦时会按产品规则取消，测试应恢复前台和佩戴感应后重跑，不能通过忽略失焦继续累计结果。

简易面板脚本通过开发版本的 VrUiInput.Dispatch 指针驱动测试真实 uGUI 命中、按压和悬停链路，并通过 ui-stick 驱动与真实手柄共用的摇杆处理路径，属于模拟输入的真机功能检查，不等同于物理扳机或佩戴体验验收。开发入口仅在 Development／Editor 构建中存在，使用原有应用私有命令通道。测试只读取生成样片，原始结果在忽略的 Docs/Validation/，截图在忽略的 Docs/Captures/。每次结束均逐字节恢复播放器设置；临时启用 USB 常亮后恢复原值并核对，不修改 screen_off_timeout。

## 验证记录

2026-10-10（Asia/Shanghai），Unity 6000.3.23f1，Android ARM64 Development／IL2CPP，USB PICO 4（A8110）。本次完善倍速入口、面板高度、远近专用重置和隐藏界面时的摇杆调节，没有改动原生 Java／GLES 插件或依赖。

| 检查 | 结果与覆盖 |
| --- | --- |
| EditMode 全套 | 184／184 通过，0 跳过，5.75 秒；包含原有 164 项回归，以及远近重置保留格式、摇杆调节与保存、界面和生命周期屏蔽等新增检查 |
| 投影 Shader 实际渲染 | 六种投影 × 三种立体布局 × 三档缩放 × 两眼，共 108 组合检查眼别；经纬度渐变纹理检查 VR360 的缩放方向、0／45／179° 转头中心和 1× 恒等 |
| Android 构建 | 01:09 完成，0 错误，11 条既有 PICO／Pipeline 包警告；最终 APK 已安装到 USB PICO |
| 简易面板设备检查 | 17／17 通过；播放器设置逐字节恢复、USB 常亮原值恢复均已核对；使用真实 uGUI 链路与公共摇杆处理路径的开发模拟输入 |
| 现有播放改善／相邻播放 | 2026-10-09 原始面板版本的 APK 上 21／21 和 8／8 通过，设置恢复已核对；本次 APK 的上下集显示状态由简易面板设备脚本复跑 |

本次原始记录为忽略目录下的 quick-controls-followup-unity-tests.json、quick-controls-followup-apk-build.json 和 quick-controls-device.json；2026-10-09 的记录为 quick-controls-unity-tests.json、quick-controls-apk-build.json、quick-controls-device-2026-10-09.json、improvements-device.json 和 adjacent-device.json。测试使用生成短样片，没有读取真实影片；本次不包含 LAN／SMB 连通性或长影片稳定性测试。

APK 为 Builds/LsVRPlayer.apk（148,192,426 字节），SHA-256：85c1edd8c978bec50f009e9ed5c2847a37c1b81e0d7e2492241032893752e820。11 条包警告涉及未配置 appID／Pipeline、PICO 的 HPE／Outline Shader、手部预制体缺失脚本、原生库 16 KB 对齐及 Windows 插件平台标记；本次未改动这些包。Unity Console 没有错误。

简易面板设备检查覆盖：空白处按压呼出并消费按压、音量连续悬停／设置／静音恢复、暂停状态下进度跳转、模式及全景缩放、倍速、关闭全局自动加载后的 Auto 字幕恢复、独立 SRT 浏览与手动字幕关闭／恢复、上下集保持简易面板、暂停保持与播放空闲隐藏、完整面板替换简易面板。新增检查通过左右摇杆分别验证全景／平面上下调节、回中停止、0.5–2×／1.2–10 米上下限、面板显示时屏蔽，以及两种远近重置保留投影和立体布局。

此前三次设备运行出现暂停／输入中断后超时，均已恢复播放器设置；第三次也核对了 USB 常亮恢复。保持佩戴感应有效、播放器前台后，重新启动完整 17 项测试并全部通过；中断记录保留在忽略目录的 quick-controls-followup-first-attempt.json、quick-controls-followup-second-attempt.json 和 quick-controls-followup-third-attempt.json，没有将它们累计为一次连续验收。

已逐张检查抬高后的控制条、音量展开区、字幕、模式及“倍速”弹窗，以及 0.5／1／2× 双眼截图。模式标题旁没有全局重置，远近行提供独立重置；控制条最后一个图标为速度表。PICO 黑色等待画面会重试，不能用作渲染验证证据。下列审核后的原始双眼截图选自本次同一 APK 的测试运行，以朝向稳定、能看清完整面板的截图展示布局；只显示生成样片及非私密文件名，不包含私人影片或共享配置。

![音量悬停展开](Images/quick-controls-volume.png)

![模式及 2× 全景缩放](Images/quick-controls-mode.png)

![字幕开关、偏移和选择](Images/quick-controls-subtitles.png)

![倍速标题、速度表入口和快捷速度](Images/quick-controls-speed.png)

物理扳机射线定位、实际悬停与拖动手感、文字可读性、转头缩放以及双眼舒适度仍需佩戴头显确认。本次自动化和截图检查与佩戴验收分别记录，不能建立这些主观结论。字幕和菜单大小／距离不参与视频缩放；设备截图用于检查它们在不同倍率下的布局。
