# L's VRPlayer 使用与构建

开发场景：`Assets/VRPlayer/Scenes/Player.unity`。Android 包名：`com.linxyi.LsVRPlayer`。当前为个人使用的开发版 APK，ARM64 / IL2CPP / GLES3 / PICO Multiview。

## 在 PICO 4 上使用

1. 安装 `Builds/LsVRPlayer.apk` 后，从未知来源应用打开 **L's VRPlayer**。
2. 在“文件”页选择“存储权限”，允许读取视频；进入“设备存储”或“Movies 视频”。“授权目录”可调用系统目录选择器。
3. 手柄扳机点击文件，按住拖动列表或使用摇杆连续上下滚动。左右手摇杆横拨均可快退／快进 10 秒，保持 0.5 秒后每 0.3 秒重复；回中停止。A / X 或菜单键开关完整面板；完整面板关闭时，空白处扳机开关简易播放控制条。控制条上半部可点击／拖动跳转，音量图标悬停展开音量条，点击切换静音。简易弹窗提供字幕、模式／全景缩放和倍速；模式页“重置远近”只恢复距离／缩放，保留影片格式；详见 [简易面板与验证](QuickControls.zh-CN.md)。面板隐藏时也可横拨跳转，短暂显示目标时间；上下拨则拉近／放大或拉远画面。输入文字、弹窗、拖动或失焦时横拨暂停响应。
4. 普通电影使用“平面 / 曲面”。立体电影选择“左右 SBS”或“上下 OU”，按源文件选择 Half / Full；VR 视频再选择 180° / 360°。默认增强识别会结合容器元数据、明确文件名标签和少量画面分析；手动设置逐片记忆。投影页的“重新识别格式”解除该片手动锁定，“撤销本次识别”恢复原格式。
5. “交换左右眼”修正左右眼顺序，“单眼 2D”从立体源取单眼。“正前方”水平居中，“躺卧”对齐当前视线；屏幕距离、大小、高度和曲率可调整。
6. 设置页可开启“自动加载同名 SRT”（默认关闭），优先中文后缀、无后缀、其他已识别语言或字幕标签，同组按文件名排序。损坏字幕自动尝试下一候选；手动选择和关闭状态逐片记忆，优先于全局自动规则。字幕页“恢复自动选择”可恢复该片自动模式。其他命名可在文件页切换到字幕文件后手动选择。
7. SRT 自动识别 UTF-8、UTF-8 BOM、UTF-16 BOM，UTF-8 无效时尝试 GB18030；乱码时手动切换编码。正字幕延后值表示更晚出现，可调大小、高度、距离、跟随视线。
8. “设置”可选速度、循环、目录连播、自动隐藏、音轨或内嵌文本字幕。暂停、快进快退、拖动进度、音量、上一部/下一部、最近播放和收藏均在界面中。播放时点击上一部/下一部，会在当前影片所在目录切换，并继承投影类型、SBS/OU、Half/Full、左右眼顺序及鱼眼角度，覆盖目标影片原格式并保存；自动识别不会覆盖继承结果，可点“重新识别格式”解除固定。目标影片自己的距离、亮度、单眼 2D、字幕及续播进度保留。直接从文件页/历史打开、播放结束后的自动目录连播仍按各片保存设置或识别结果播放。最近播放每行的“所在目录”会打开并定位影片，不中断当前播放；路径或授权失效会提示，保留历史记录。

SMB：进入 SMB 来源会自动发现局域网服务器，点击设备后选择共享；需要认证时填写账号、密码和可选域。手动配置入口仍可填写主机/IP、共享名（例如 `Movies`，不必填写完整网络路径）和端口，端口通常为 445；匿名共享可留空用户名。支持编辑/删除配置；记住密码时使用 Android Keystore 加密保存。不保存密码时，重启应用后需在编辑配置中补填。没有启用 SMB1。测试用的 `127.0.0.1:1445` 连接只在开发机测试服务器和 ADB 转发运行时有效，实际 NAS 使用局域网地址。

Emby：文件页选择同级的 Emby 来源，可登记多个服务器和账户；添加时切换 HTTP / HTTPS，分别填写地址或 IP 与端口，默认端口 8096。首页显示媒体库、继续播放和最新加入，媒体库内全部影片放在连续滚动的海报墙中；演员图片可打开人物简介与出演电影，类型、标签和制片厂可打开关联影片。

Emby 使用现有播放器播放原始视频并保存各项 VR 设置。分 CD 的“额外部分”在演员表上方，支持同片切换和开启连播后自动接段；附加篇在演员表下方，只支持组内显式切换，不自动连播。上一部 / 下一部不会跳到其他 Emby 电影；单片循环仍优先于连播。登录、编码限制和复现命令见 [Emby 媒体服务器](Emby.zh-CN.md)，已完成的检查与剩余范围见 [Emby 接入验证](EmbyValidation.zh-CN.md)。

摘下头显、失去焦点或进入后台会暂停，回来后手动播放。退出或切换影片时保存进度，播放过程中也周期保存。错误会显示在状态栏，可点击重试。

0.6 的无标签识别会分别判断 SBS/OU 和投影，最多等待 60 秒有效播放以越过片头；暂停时不消耗采样窗口。投影页会显示“继承／手动格式”“有效画面不足”或继续判断投影等原因。无标签 VR180 目前保留候选与“采用建议”，尚未通过独立真实集的自动投影验收。同类影片目录可显式开启“本目录优先 VR180”，仅在已确认 SBS 后补齐 180°；元数据和明确文件名仍优先。详见 [VR180 专项实测](VR180SbsRecognitionResults.zh-CN.md)。

## 返回与目录位置

左手 Y／右手 B 全局返回，编辑器键盘 Esc 使用相同逻辑：先退出虚拟键盘、二级菜单或弹窗，再沿实际访问历史返回，历史用尽后关闭面板。从本地、收藏等首页直接进入深层目录后，一次返回该首页及原滚动位置，不必逐层退到文件系统根；“上级”按钮始终进入物理父目录。返回还原那次访问的搜索、排序和视频／字幕模式，刷新和读取失败后重试也保留列表位置。切换来源会清除旧来源浏览历史，切换顶部标签保留文件历史。

主面板“手动选择 SRT”先返回本次选择流程中访问的目录，再返回字幕页，进入选择前的文件历史仍保留。简易字幕浏览返回打开它的弹窗，不替换主文件来源或播放列表。长按 Y／B 或双手同时按下只返回一步；失焦、后台或手柄追踪中断后需松开再按，拖动进度中返回会取消该次拖动。

## 从源码构建

需要 Unity 6000.3.23f1 及 Android Build Support（SDK、NDK、OpenJDK）。Gradle 依赖首次需要网络。

```powershell
# 在项目根目录运行；Unity 安装位置不同时传 -UnityEditor
./Tools/Build-Native.ps1
```

等待 Unity 导入与编译完成，在菜单执行 **VRPlayer → Configure Project and Open Player**，随后 **VRPlayer → Build Android APK**。产物为 `Builds/LsVRPlayer.apk`。不要把 Java 源码直接当作 Unity 脚本；修改 `Native` 后需重新运行原生构建脚本。

项目不包含 XRI Toolkit 的 Starter Assets、Device Simulator、DemoScene 或模板 SampleScene；正式 APK 只包含 Player 场景。PICO 依赖已固定提交，运行时、编辑器工具和测试程序集分离。

## 验证工具

- EditMode：Unity Test Runner 中运行 `VRPlayer.Tests`。
- 返回与目录位置：`python Tools/Device-BackNavigationTest.py --serial <PICO_SERIAL>`，需要已安装 Development APK，以及 `Tools/Generate-RecognitionMedia.py --clips` 生成并推送到 `Movies/VRPlayerTest/Recognition` 的视频和 SRT。脚本创建独占的目录／收藏夹具，通过共享输入路径检查 Y/B、物理上级、历史返回、位置恢复和字幕入口，结束时核对原始设置、命令文件及夹具清理。
- 简易控制：`python Tools/Device-QuickControlsTest.py --serial <PICO_SERIAL>`，使用相同 Recognition 夹具，默认完整运行 17 项。两类测试需保持焦点、有效佩戴感应与新鲜诊断；中断后完整重跑，模拟输入不替代真实按键／佩戴体验确认。最新结果和验证边界见[全局返回与简易控制验证](QuickControls.zh-CN.md#验证记录)。
- 原生 JUnit：使用 Unity 随附的 OpenJDK 与 Gradle，从项目根目录运行以下命令；安装位置不同时调整第一行。报告输出到被忽略的 `Native/Android/build/reports/tests/testDebugUnitTest/`。

```powershell
$unityAndroid = 'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\PlaybackEngines\AndroidPlayer'
$env:ANDROID_HOME = Join-Path $unityAndroid 'SDK'
$env:JAVA_HOME = Join-Path $unityAndroid 'OpenJDK'
& (Join-Path $unityAndroid 'OpenJDK/bin/java.exe') -classpath (Join-Path $unityAndroid 'Tools/gradle/lib/*') org.gradle.launcher.GradleMain -p Native/Android testDebugUnitTest --console=plain
```

- `Tools/Generate-TestMedia.py` 生成基本样片，`--extended` 增加电影音轨、内嵌 SRT、4K60 HEVC。需要开发机 Python 的 `imageio-ffmpeg`，可安装到被忽略的 `Tools/.runtime`。
- 将生成的 `.mp4`、`.mkv`、`.srt` 推送到设备 `Movies/VRPlayerTest`；不要把 4GB 偏移测试文件推送到设备。
- `Tools/Device-SmokeTest.py` 运行设备回归并写入 `Docs/Validation`；依赖授权 ADB、已启动的开发 APK，以及 Pillow（双眼图片计数）。`--section local|stereo|audio|smb` 可分组复测。连接多台设备时传 `--serial` 明确选择 PICO；PowerShell 命令脚本对应参数为 `-Serial`。
- `Tools/Device-LifecycleTest.py --serial 设备序列号` 验证后台暂停与进程重启后恢复；`Tools/Device-StabilityTest.py --serial 设备序列号 --cycles 18` 验证高分辨率及反复切片。测试期间保持头显唤醒、停留在播放器中；系统边界界面或摘下头显可能暂停 Unity 更新，此时超时不能被记录为播放通过。
- SMB 测试：开发机安装 Impacket 后运行 `Tools/Serve-TestSmb.py`，建立 `adb reverse tcp:1445 tcp:1445`（多设备加 `-s`），再运行设备测试的 `--smb` 或 `--section smb`。`SmbSmokeTest.java` 检查协议随机读取和 4GB 偏移，依赖目录可用 Gradle 的 `collectRuntimeDependencies` 任务生成。`Generate-TestMedia.py --large-offset` 可重建大文件偏移样本，测试后可删除以释放磁盘；`--8k` 可生成 8K30 短片。
- `Tools/Device-Command.ps1` / Python 回归脚本通过 `adb run-as` 写入应用**内部私有目录**；命令入口仅编译进 Development / Editor 版本，未注册外部命令接收器。

设备诊断位于应用外部文件目录的 `playback-diagnostics.json` 和 `decoder-capabilities.json`。设备能力范围不是对任意分辨率、帧率和码率组合的播放保证；实际样片结果见验证报告。

SMB 播放采用 1 MiB 按需预读，信息页显示剩余缓冲、加载状态、意外缓冲和网络读取统计；后台事件记录在同目录的 `playback-events.jsonl`。使用 `Tools/Device-SmbPlaybackTest.py` 可对比原版、仅诊断和预读三组，命令、指标定义与限制见 [SMB 播放预读与诊断](SmbPlaybackDiagnostics.zh-CN.md)。

`Docs/Validation/` 与 `Docs/Captures/` 是本机测试生成的原始记录和截图，已忽略，不随源码提交；可用上述脚本重新生成。仓库保留整理后的 [v0.5.0 改进与验证报告](PlaybackImprovements.zh-CN.md)、[v0.4.0 验证报告](ExperienceValidation.zh-CN.md) 和 [播放内核验证报告](ValidationReport.zh-CN.md)。APK、测试视频、工具运行环境和本机 `.codex` 配置同样不入库；必要的原生插件、字体、Unity 资源及 `.meta` 文件通过 Git/Git LFS 保留，克隆时需安装 Git LFS 并执行 `git lfs pull`。

## 0.5 字幕、历史与格式识别

- 设置升级到版本 3，先执行原有 v1→v2 字幕尺寸迁移，再迁移到 v3。旧影片有字幕路径时视为手动选择，无路径时进入自动模式；旧投影视为已保存设置，不会在升级后被自动识别覆盖。
- 自动字幕只匹配完整片名之后的白名单标签。中文覆盖 `chs/cht/zh/zh-CN/zh-TW/zh-Hans/zh-Hant/chi/zho`、简体／繁体中文／中英双语等标签和 `chs&eng` 双语字幕。不会把续集、其他集数或影片名称里的 `zh` 当作中文字幕后缀。
- 格式识别提供“增强识别／仅标签与元数据／关闭”。增强模式为默认值；独立记录投影、立体排列、Half/Full、眼序，未知字段继续使用默认值。`SBS` 不等于 VR，普通 2:1 视频不会仅凭比例切换成 360°。
- 证据按用户手动设置、受支持元数据、明确文件名、多帧画面、宽高比的次序应用。同级冲突保留未知并提示。投影页显示来源、候选及采用建议入口；手动修改会取消当前分析，保留屏幕距离、亮度和字幕设置。
- 元数据探测独立打开文件，结构化跳过媒体负载，最多读取 2 MiB、3 秒；覆盖 MP4 `st3d/sv3d`、旧球面标签、MKV/WebM 立体与投影字段。超时、不可定位、损坏或不兼容网格时退回可用证据。
- 首个有效画面后累计正常播放 10 秒内最多分析 8 帧，采样间隔至少 1 秒，最长边 512 像素；暂停和失焦不消耗分析窗口。黑场片头最多等待 30 秒有效播放时间。画面自动应用要求至少 3 帧一致、评分 ≥0.90、领先次选 ≥0.15，每次播放最多一次。
- 当前画面自动应用仅开放通过独立生成样片验证的 SBS/OU。180°/360°/鱼眼画面分析提供建议；Cubemap/EAC 依赖明确标签或兼容元数据，不能从六块画面推断任意面顺序。规则评分不是准确率；样片统计与剩余限制见 v0.5 报告。
- 重现画面验证：运行 `python Tools/Generate-RecognitionMedia.py --clips`，Unity 菜单 **VRPlayer → Validate Frame Recognition** 输出 `Docs/Validation/frame-recognition.json` 并更新算法版本匹配的自动应用配置。`--metadata-only` 补生成真实容器元数据样片。测试通过后再构建 APK。
- 真机回归：推送 `TestMedia/Recognition` 中的视频和 SRT 到 `Movies/VRPlayerTest/Recognition`，运行 `python Tools/Device-ImprovementsTest.py --serial <PICO序列号> --host <测试电脑IP>`。真实 LAN 服务使用上述 `Serve-LanTestSmb.py`；将 Recognition 样片复制到 `TestMedia/LanFixtureShare/Recognition`。脚本备份并恢复设备设置，备份仅保留在被忽略的验证目录，勿提交或分享。
- `Tools/Device-RecognitionStressTest.py --serial <PICO序列号>` 比较增强识别开关的 8K 短片表现，并检查快速切片、后台恢复、进程重启后的进度与字幕。需要推送生成的 `Performance.8K30.HEVC.mp4`；该测试不代表持续热稳定性验收。

## 0.4 界面与局域网发现

- 设置页提供均衡 1.0×、高清 1.25×、超清 1.5×，默认高清，使用 4× MSAA 和 72Hz 目标刷新率。信息页显示设备实际眼缓冲尺寸、倍率与应用帧率；视频分辨率单独显示。运行时拒绝请求倍率时回退较低档。
- 进入文件页的 SMB 来源后自动进行一次局域网扫描，支持重新扫描和停止。扫描组合 mDNS、WS-Discovery 和实际 IPv4 子网内的 SMB2/3 协商，不需要逐个输入 IP。最多并发 32，连接超时 500ms，单轮 30 秒，大网段未完成时明确提示。
- 点击发现的设备可匿名列出共享；服务器要求认证时填写账号密码和域。也可手填共享名。选择共享后保存并打开；原有手动服务器配置与 `smb://配置ID/路径` 收藏继续有效。设备隔离、组播被路由器阻断或服务器防火墙仍可能影响发现，可改用手动配置。
- 文件列表使用滚动方式。食指扳机点击选择，按住后移动射线拖动；摇杆上下连续滚动。拖动后松手不会打开条目。收藏行右侧可直接移除，并在 5 秒内撤销。
- 字幕分为选择、位置、样式三页。距离支持 0.1–6 米及 0.01 米微调，距离变化保持视觉大小和相对高度。字号和视觉高度可单独调整。
- 字幕提供白字黑边、黄字黑边、阴影、半透明底四个预设，以及独立的颜色、描边、阴影强度、背景样式与透明度；样式页实时预览。外置 SRT 与已支持的内嵌文本字幕使用相同样式。
- 旧设置自动迁移到版本 2，保留历史和收藏，并换算原字幕大小与高度以保持原视觉效果。
- 新增 `Tools/Device-ExperienceTest.py --serial <PICO序列号> --host <测试电脑IP>`，验证画质档位、标签切换、局域网发现、共享枚举和取消。真实 LAN 测试可运行 `Tools/Serve-LanTestSmb.py --bind <本机局域网IP> --duration 600`，开发依赖为 Impacket 与 zeroconf；仅暴露生成的测试媒体。
- 认证测试服务器加 `--port 1446 --auth`，设备测试加 `--port 1446 --username vrplayer --password fixture-test`，同时验证匿名拒绝、错误密码与正确密码。此账号仅用于公开生成的样片。`Tools/SmbSharesSmokeTest.java` 可在桌面 Java 上独立验证匿名 SRVSVC 与依赖兼容。
- `Tools/Device-CaptionTest.py --serial <PICO序列号>` 捕获五档字幕距离和四种预设，需人工检查双眼截图；截图生成不代表视觉验收。`Docs/ExperienceValidation.zh-CN.md` 记录本轮验证结果和未验收项。

## 当前格式边界

核心路径为 Media3 → Android 硬件解码 SurfaceTexture → GPU 纹理 → 每眼投影。播放纹理不经 CPU 搬运；增强识别会异步回读少量缩小画面进行后台分析。尚未接入 PICO 外部合成层，也未实现 HDR 色调映射。当前针对 SDR 文件。

VR180/360 等距柱状投影、SBS/OU 与 Half/Full 是主要支持目标。鱼眼为单个前向圆形鱼眼，支持设置视场角；Cubemap / EAC 目前只提供上排 `+X -X +Y`、下排 `-Y +Z -Z` 的 3×2 布局。其他排列、双鱼眼拼接、相机专用 VR180 网格、MVC/蓝光 ISO、DRM、Dolby Vision 均不作为已支持格式。

文件列表能看到扩展名，不代表内核一定能解码它。视频编码取决于 PICO 解码器；FFmpeg 扩展补充音频。字幕以外置 SRT 和内嵌文本字幕为主，未实现完整 ASS 特效和图形字幕。真实 NAS 的无线吞吐、高码率 8K、长时间发热和佩戴舒适度需要单独验收。
