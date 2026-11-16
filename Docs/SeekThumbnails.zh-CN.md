# 进度条画面预览

完整面板和快捷面板都支持将鼠标或手柄射线悬停在进度条上查看缩略图与目标时间。拖动时预览随目标位置移动，只有松手才提交跳转。离开进度条、关闭面板、打开其他影片或失去焦点会取消预览；取消的拖动不会提交跳转，另一只手不能接管已开始的拖动。

Android 使用独立的 `MediaMetadataRetriever` 取帧，支持本地文件、已授权的 `content://` 文件、SMB 和 Emby 原始视频。SMB 使用独立连接；Emby 复用只通过请求头认证且拒绝跨源重定向的范围读取实现。编辑器使用独立且静音的 Unity VideoPlayer 预览本地文件，主播放器不会为缩略图 seek。编辑器的 Emby 播放限制保持不变。

左右/上下立体画面取单眼，并遵循交换眼序设置；平面视频保持显示比例。全景影片显示原始投影图的单眼缩略图，不随头部方向旋转，也不叠加字幕。

缩略图保留原有 344 × 244 外框尺寸、12 像素内边距与底部时间区域，将内外圆角一起放大以改善 VR 视角下的可见性。外半径 30，图片与底板的内半径由 `30 - 12 = 18` 计算，遵循“外半径 = 内半径 + 间距”。320 × 180 图片填满预览区域时，顶部左右两角与外框分别共享圆心；底部保留时间栏，不能与整张卡片的底角共享圆心，图片四角统一使用半径 18。图片由 `RoundedRawImage` 直接生成圆角网格，不依赖透明度阈值的模板遮罩。其他比例的图片继续居中适配，圆角限制在图片短边的一半以内，底板与外框布局不变。完整和快捷面板使用同一组件。

## 性能和降级

- 预览目标使用空间死区：围绕上次接受的位置，半径为进度条宽度的 0.5%，最多 4 个 UI 布局单位。死区内的手抖不更新取帧目标；小幅越界稳定 180 ms 后更新，连续越界的防抖等待最多 350 ms；位移达到死区半径的 4 倍时立即接受新目标。这些时间仅用于目标选择，不包括网络读取和解码。
- 悬停后等待 160 ms 合并取帧请求；同一 2 秒区间复用结果。移动射线只更新待处理目标，不重启已经执行的读取或反复推迟第一次请求。时间文字、光标位置和松手提交的跳转仍使用未过滤的准确位置。
- 加载下一位置或该次取帧失败时保留上一张有效图片，连续拖动也会逐步显示已经完成的样本；较早的请求不能覆盖已经命中的新目标缓存。图片对应的区间与光标不同时，在图内标注“预览 时间”，底部仍显示准确的跳转目标。Android 取邻近关键帧，因此标注表示取帧请求时间，不保证逐帧对应实际关键帧。
- 每部当前影片最多保留 24 张、不超过 320 × 180 像素的图片；不写入磁盘。新播放代次、停止和销毁时释放缓存。
- Android 只有一个预览解码任务和一个可替换的等待任务。使用 512 KiB 范围块和最多 8 MiB 的 LRU 缓存，避免解析长片时在索引和帧数据间反复重读。每次请求最多读取 128 MiB（不是分配 128 MiB 内存）；从入队起 20 秒超时取消，Unity 等待上限 25 秒。它们不计入主播放网络吞吐，也不修改播放缓冲阈值。
- 同一播放代次连续预览复用已经解析的索引、范围缓存和独立连接；每次取帧重新计算传输预算。闲置 5 秒、切片、失败或取消时释放。SMB 保持有限请求期限，不用空闲 socket 超时打断正在解码的预览；HTTP 仍只通过请求头认证并拒绝跨源重定向。
- 快速拖动合并为最新目标；离开进度条、切片和隐藏面板取消任务并丢弃迟到结果。网络关闭与解码资源释放在后台完成。
- 不支持的编码/容器、平台无法同时提供两个解码器、网络超时或超出读取预算且没有可保留的图片时显示“预览暂不可用”，时间和跳转仍可使用。高分辨率长片的性能仍需按真实设备和片源验收。

2026-11-16 在真实 Emby 4K 高码率长片上定位并修复了旧实现的重复读取超限：原先 9/9 个取帧位置失败，修复后 9/9 成功，同时进行 Media3 原始视频解码的 9 个位置也通过。首次解析仍可能需要数秒，连续预览可复用索引。范围、延迟、复现命令和限制见[真实网络缩略图修复记录](SeekThumbnailNetworkFix.zh-CN.md)。

2026-11-17 用户反馈真实 8K 也能取到缩略图，但手抖和加载延迟仍影响可见性，因此追加上述死区、防抖和保留画面逻辑。179 项相关 EditMode 用例已覆盖，两种面板的准确松手跳转不受影响；保留画面时间标注完成中英日三种语言的编辑器渲染检查。真实 8K 的成功属于用户反馈，本次交互改进的物理手柄体验仍待用户测试，详细轮次见同一修复记录。

## 可复现验证

先生成只包含红、绿、蓝时间段的 12 秒合成片：

```powershell
python Tools/Generate-ThumbnailFixture.py
```

Unity 6000.3.23f1 Test Runner：

- EditMode：`VRPlayer.Tests.SeekThumbnailTests`、`VRPlayer.Tests.QuickControlsTests`，覆盖时间边界、LRU 上限、长片手抖死区、稳定及有上限的防抖、连续拖动保留画面、失败保留与时间标注、新缓存不被旧帧覆盖、未量化的松手跳转、代次取消、迟到帧释放、眼序、双手所有权和取消拖动。`VRPlayer.Tests.LocalizationTests` 校验三种语言和格式占位符。
- EditMode：`VRPlayer.Tests.SeekThumbnailRenderingTests`，实际以 50% 显示尺度渲染生产缩略图组件，逐个检查四角露出底板、图片中部可见、12 像素内边距和外框保留，覆盖普通图片与单眼 UV 裁切；同时校验半径差和顶部两对圆心重合。原来的模板遮罩会让四角像素仍被图片覆盖，内外同为半径 18 则不满足同心关系，两种情况均会被拒绝。
- PlayMode：`VRPlayer.Tests.SeekThumbnailPlaybackTests`，实际解码 9 秒处蓝色画面，确认主播放器仍停在原位置。

构建 Android 单元测试、测试包和运行库：

```powershell
$android = 'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Data\PlaybackEngines\AndroidPlayer'
$env:ANDROID_HOME = Join-Path $android 'SDK'
$env:JAVA_HOME = Join-Path $android 'OpenJDK'
& "$env:JAVA_HOME/bin/java.exe" -classpath "$android/Tools/gradle/lib/*" org.gradle.launcher.GradleMain -p Native/Android testDebugUnitTest assembleDebugAndroidTest assembleRelease --console=plain
python Tools/Device-ThumbnailTest.py --serial <PICO_SERIAL> --smb
```

设备脚本要求 `com.linxyi.vrplayer.test` 原本未安装、PC 的 1445 端口空闲，以及 `Tools/.runtime` 中已有 Impacket。它只安装独立测试包、启动自己拥有的只读合成 SMB 服务并创建必要的 `adb reverse` 映射；最后卸载测试包、结束自己的服务进程并验证删除新增映射。已有其他测试包会让它提前退出，清理失败会使运行失败。脚本不修改正式播放器数据、头显休眠或 USB 常亮设置。

设备用例覆盖不同时间的真实解码、连续 Media3 播放时独立预览、替换/取消/错误代次、HTTP 请求头认证与跨源拒绝、挂起网络读取的取消，以及 SMB 预览释放后其他连接仍能读取。HTTP 为设备内回环服务器；SMB 为 `adb reverse` 合成测试，不是实际局域网、真实 Emby 账户或长片兼容性验收。

原始记录在忽略目录 `Docs/Validation/SeekThumbnails/`，批量 UI 截图在 `Docs/Captures/SeekThumbnails/`。可通过 `VRPlayer.Editor.UIRedesignPreview.ExportBatch` 重现界面布局截图。

### 完整播放器实机界面测试

在 Unity 菜单选择 **VRPlayer → Validation → Build Thumbnail Test APK**，或在关闭编辑器后运行：

```powershell
& 'C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor\Unity.exe' -batchmode -quit -projectPath (Get-Location).Path -executeMethod VRPlayer.Editor.DeviceValidationBuild.BuildThumbnails -logFile 'Docs/Validation/SeekThumbnails/ui-build.log'
```

该入口将当前运行时代码与生产场景构建为 `Builds/ThumbnailValidation.apk`，应用 ID 为 `com.linxyi.LsVRPlayer.thumbnailtest`，可与正式签名的播放器并存。准备合成素材并运行：

```powershell
python Tools/Generate-ThumbnailFixture.py
python Tools/Generate-TestMedia.py --extended
python Tools/Device-ThumbnailUiTest.py --serial <PICO_SERIAL>
```

需要 Pillow（可安装在 `Tools/.runtime`）、设备已接受同一版本的隐私说明，以及全程佩戴、保持应用焦点和新鲜诊断。脚本只在独立测试包中写入生成素材与测试设置，最后卸载自己的测试包并校验清理命令暂存目录；保留正式播放器数据及电源设置。测试包原本存在时提前退出，避免覆盖其他测试数据。

13 项检查覆盖快捷/完整进度条预览、悬停换帧、主播放位置不变、双手拖动所有权、松手提交与切面板取消、切片代次、停止后隐藏、SBS/OU 交换眼序，以及普通/4K/8K 合成短片播放时预览。截图使用 `adb shell screencap` 捕获头显合成画面；测试输入由开发版驱动进入共享 `VrUiInput` 与实际 uGUI 路径，不代表物理手柄验收。报告记录 APK、预览组件、圆角图片组件与测试脚本的 SHA-256，原始结果及失败轮次保存在 `Docs/Validation/SeekThumbnails/FullApp/`。这 13 项功能检查不自动判断圆角，图片四角另外由渲染测试和实机截图放大复核。

## 界面示例

以下为 2026-10-27 的 PICO 实机合成画面，使用生成色卡和合成影片信息。外框与留白保留，缩略图在外框内部绘制圆角。局部图仅裁剪、放大 3 倍，来源和坐标见 [实机验证报告](SeekThumbnailDeviceValidation.zh-CN.md)。

![实机圆角局部](Images/SeekThumbnail-Corners.png)

![快捷面板预览](Images/SeekThumbnail-Quick.png)

![完整面板预览](Images/SeekThumbnail-Full.png)

## 验证边界

以下为 2026-10-27 圆角修复阶段的历史验证，不代表 2026-11-17 防抖改动重新完成了这些设备测试；最新结果见上文链接的网络与交互修复记录：

| 检查 | 结果 |
| --- | --- |
| Unity 6000.3.23f1 编译及 Android Development APK | 本次成功，`Builds/ThumbnailValidation.apk` |
| EditMode 缓存、圆角渲染及进度条/快捷控制相关回归 | 本次 130/130 通过，含新增的两项四角与边框像素检查；提交前完整 EditMode 套件 806/806 通过 |
| PlayMode 本地视频真实预览、主播放位置不变 | 基础实现阶段 1/1 通过；本次未重跑 |
| Native JUnit 现有回归 | 基础实现阶段 58/58 通过；本次未重跑 |
| PICO A8110 独立原生测试包 | 基础实现阶段 6/6 通过，含 SMB 合成回环测试；本次未重跑 |
| 圆角修复后完整播放器 PICO 界面回归 | 本次 13/13 通过，含完整/快捷面板、停止、眼序及 4K/8K 短片 |
| 实际 uGUI 布局截图 | 本次完整/快捷两种面板已检查；局部放大确认图片四角和边框 |
| 设备清理 | 测试包、新增 reverse 映射、拥有的 SMB 服务均已清理；未修改播放器/电源设置 |

Android 运行库已重新构建并更新到 `Assets/Plugins/Android/vrplayer-android.aar`。预览使用 Android 系统和现有 Unity/Media3/SMBJ 能力，没有新增第三方依赖。Android API 契约见 [MediaMetadataRetriever](https://developer.android.com/reference/android/media/MediaMetadataRetriever) 与 [MediaDataSource](https://developer.android.com/reference/android/media/MediaDataSource)。

圆角修复后的设备测试使用 `Builds/ThumbnailValidation.apk`；本轮未覆盖安装正式应用。`Builds/LsVRPlayer.apk` 是基础实现阶段的旧构建，不包含本次圆角修复。具体时间、短片指标、修复及清理记录见 [实机验证报告](SeekThumbnailDeviceValidation.zh-CN.md)。报告同时更正了先前对半径 6 遮罩和内外同为半径 18 的视觉判断，旧的功能测试通过不等于图片圆角及同心关系验证通过。

物理手柄的实际射线/扳机、佩戴时清晰度、舒适度、不同厂商硬解资源以及长时间 4K/8K 影片播放需要单独验收。合成短片和编辑器截图不能代替这些检查。
