# L's VRPlayer 使用与构建

开发场景：`Assets/VRPlayer/Scenes/Player.unity`。Android 包名：`com.linxyi.LsVRPlayer`。当前为个人使用的开发版 APK，ARM64 / IL2CPP / GLES3 / PICO Multiview。

## 在 PICO 4 上使用

1. 安装 `Builds/LsVRPlayer.apk` 后，从未知来源应用打开 **L's VRPlayer**。
2. 在“文件”页选择“存储权限”，允许读取视频；进入“设备存储”或“Movies 视频”。“授权目录”可调用系统目录选择器。
3. 手柄扳机点击文件，按住拖动列表或使用摇杆连续上下滚动。A / X 或菜单键呼出/隐藏面板；面板隐藏时视频继续播放。
4. 普通电影使用“平面 / 曲面”。立体电影选择“左右 SBS”或“上下 OU”，按源文件选择 Half / Full；VR 视频再选择 180° / 360°。文件名中的 `180`、`360`、`SBS`、`OU` 等会提供初始设置，允许手动覆盖并记忆。
5. “交换左右眼”修正左右眼顺序，“单眼 2D”从立体源取单眼。“正前方”水平居中，“躺卧”对齐当前视线；屏幕距离、大小、高度和曲率可调整。
6. “字幕”页列出同目录同名候选，例如 `电影.srt`、`电影.chs.srt`、`电影.zh-CN.srt`。首次由用户选择，后续恢复该影片的选择。其他命名可在文件页切换到字幕文件后手动选择。
7. SRT 自动识别 UTF-8、UTF-8 BOM、UTF-16 BOM，UTF-8 无效时尝试 GB18030；乱码时手动切换编码。正字幕延后值表示更晚出现，可调大小、高度、距离、跟随视线。
8. “设置”可选速度、循环、目录连播、自动隐藏、音轨或内嵌文本字幕。暂停、快进快退、拖动进度、音量、上一部/下一部、最近播放和收藏均在界面中。

SMB：进入 SMB 来源会自动发现局域网服务器，点击设备后选择共享；需要认证时填写账号、密码和可选域。手动配置入口仍可填写主机/IP、共享名（例如 `Movies`，不必填写完整网络路径）和端口，端口通常为 445；匿名共享可留空用户名。支持编辑/删除配置；记住密码时使用 Android Keystore 加密保存。不保存密码时，重启应用后需在编辑配置中补填。没有启用 SMB1。测试用的 `127.0.0.1:1445` 连接只在开发机测试服务器和 ADB 转发运行时有效，实际 NAS 使用局域网地址。

摘下头显、失去焦点或进入后台会暂停，回来后手动播放。退出或切换影片时保存进度，播放过程中也周期保存。错误会显示在状态栏，可点击重试。

## 从源码构建

需要 Unity 6000.3.23f1 及 Android Build Support（SDK、NDK、OpenJDK）。Gradle 依赖首次需要网络。

```powershell
# 在项目根目录运行；Unity 安装位置不同时传 -UnityEditor
./Tools/Build-Native.ps1
```

等待 Unity 导入与编译完成，在菜单执行 **VRPlayer → Configure Project and Open Player**，随后 **VRPlayer → Build Android APK**。产物为 `Builds/LsVRPlayer.apk`。不要把 Java 源码直接当作 Unity 脚本；修改 `Native` 后需重新运行原生构建脚本。

保持原有 Demo/Sample 资源；正式 APK 只包含 Player 场景。PICO 依赖已固定提交，运行时、编辑器工具和测试程序集分离。

## 验证工具

- EditMode：Unity Test Runner 中运行 `VRPlayer.Tests`。
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

`Docs/Validation/` 与 `Docs/Captures/` 是本机测试生成的原始记录和截图，已忽略，不随源码提交；可用上述脚本重新生成。仓库保留整理后的 [v0.4.0 验证报告](ExperienceValidation.zh-CN.md) 和 [播放内核验证报告](ValidationReport.zh-CN.md)。APK、测试视频、工具运行环境和本机 `.codex` 配置同样不入库；必要的原生插件、字体、Unity 资源及 `.meta` 文件通过 Git/Git LFS 保留，克隆时需安装 Git LFS 并执行 `git lfs pull`。

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

核心路径为 Media3 → Android 硬件解码 SurfaceTexture → GPU 纹理 → 每眼投影。没有 CPU 视频帧回读；尚未接入 PICO 外部合成层，也未实现 HDR 色调映射。当前针对 SDR 文件。

VR180/360 等距柱状投影、SBS/OU 与 Half/Full 是主要支持目标。鱼眼为单个前向圆形鱼眼，支持设置视场角；Cubemap / EAC 目前只提供上排 `+X -X +Y`、下排 `-Y +Z -Z` 的 3×2 布局。其他排列、双鱼眼拼接、相机专用 VR180 网格、MVC/蓝光 ISO、DRM、Dolby Vision 均不作为已支持格式。

文件列表能看到扩展名，不代表内核一定能解码它。视频编码取决于 PICO 解码器；FFmpeg 扩展补充音频。字幕以外置 SRT 和内嵌文本字幕为主，未实现完整 ASS 特效和图形字幕。真实 NAS 的无线吞吐、高码率 8K、长时间发热和佩戴舒适度需要单独验收。
