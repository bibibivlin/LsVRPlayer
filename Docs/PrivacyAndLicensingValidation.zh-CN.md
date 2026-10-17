# 隐私、权限及第三方分发材料验证

日期：2026-10-17。开发者公开展示为 [bibibivlin](https://github.com/bibibivlin) 与项目地址；开发者信息位于页面底部，隐私联系邮箱列在隐私政策末尾。

## 应用行为

- 首次启动或政策版本失效时，显示中英文摘要；同意前不初始化媒体后端和 SMB/Emby 服务。返回、切换页签或隐藏面板不能绕过确认；不同意可退出。政策版本 `2026-10-17.1` 单独保存，不改动 v1→v2→v3 播放设置迁移。
- 首次页及信息页均提供离线隐私政策、权限说明和完整第三方许可。政策/权限可切换中英文，正文可滚动；许可页支持导出随包完整对应源码。
- 开发者主页链接和项目地址位于首次页、权限说明和许可文本底部；中英文隐私政策使用“开发者 / Developer”，将开发者、项目和唯一的隐私联系邮箱集中在文末。项目地址不作为隐私请求渠道。
- 网页导出的开发者名称为 HTTPS 链接；应用内显示带下划线的可点击名称，沿用现有 Button 和 ScrollRect 输入路径。已检查首次页、政策页及许可页的显示，未在头显上实际启动外部浏览器。
- 本地媒体权限先解释用途和拒绝影响，再由用户主动选择进入 Android 系统授权。返回、暂不允许、隐藏或切换页面均不会被当成同意。
- 正式版不写详细的路径/字幕/界面诊断。Android 自动备份关闭；PICO 默认增加的修改系统设置权限和企业认证权限在 Gradle 合并阶段移除。实际权限按发布白名单校验。
- 旧 GPL Jellyfin 预编译库已从两个 Gradle 声明移除；新音频方案和分发材料见 [许可说明](Licensing.zh-CN.md)。硬件视频路径、音频后备顺序与缓冲阈值保持原有行为。

![首次确认（编辑器预览）](Images/privacy-first-use.png)

![隐私政策底部（编辑器预览）](Images/privacy-policy.png)

## 自动及设备检查

| 检查 | 结果和边界 |
| --- | --- |
| Unity 6000.3.23f1 EditMode `VRPlayer.Tests` | 585/585 通过，0 跳过；新增 11 个隐私记录和 UI 行为用例 |
| Native Android JUnit | 53/53 通过，7 个测试套件 |
| 发布工具回归 | 8/8 通过；拒绝额外权限、无上限旧存储权限、备份开启、错误签名、失败测试，以及被替换的库/源码/政策 |
| PICO 4 A8110 / PICO OS 5.13.7 / Android 10 | 独立音频测试包 6/6 通过：ARM64 JNI 加载，常见音频解码器可用，未安装软件视频 renderer，AC3/EAC3/DTS 生成正弦样本解码为非静音 PCM 并通过重置 |
| 同设备的源码导出 | 独立桥接测试包 1/1 通过：没有媒体读取授权也能导出，SHA-256 与内置 ZIP 一致，MediaStore pending 状态解除，测试拥有的 URI 删除并验证 |
| 新音频库 ELF | 四库为共享链接；所有 LOAD segment 对齐至少 16 KB；JNI 动态依赖 FFmpeg 三库 |
| 界面 | 编辑器渲染查看了首次页、中英文政策、权限及许可页，修正首次摘要与按钮的重叠 |

音频测试最初发现用例把 Media3 返回的 `LIBAVCODEC_IDENT` 当作 FFmpeg 发行版本号，以及生成的 DTS 编码包包含两个 core frame；已修正用例，按 Media3 实际提取器的帧边界逐帧送入 JNI。源码导出用例也改为在 Android 主线程构造测试宿主。这些调整不修改上游音频封装或解码逻辑。

两种设备测试使用本次新安装的独立包 `androidx.media3.decoder.ffmpeg.test` 与 `com.linxyi.vrplayer.test`，完成后均已卸载；未读写原播放器设置、历史、账户或用户影片，也没有修改系统屏幕超时。原始日志和预览保留在忽略的 `Tools/.runtime/privacy-license/`。

最终非 Development APK 构建成功：`Builds/.privacy-license-validation/LsVRPlayer.apk`，1.0.0 / 7，91,846,597 bytes，SHA-256 `8684d74936e0ad6b2a4bae11a103f4ad15abe57063c405078abe687e2364c2e7`。这是当前未提交工作区的验证产物，使用 Android Debug 签名，不是可发布的正式签名版本。旧的正式签名 APK 未被替换。

对实际 APK 的验证已通过：包名、版本、minSdk 29 / targetSdk 35、ARM64、`debuggable=false` / `testOnly=false`，7 项权限白名单（旧存储权限 maxSdk 32）、`allowBackup=false` / `fullBackupContent=false`；四个音频库与构建记录哈希一致；完整源码 ZIP 和 37 项组件清单、中英文政策/权限及许可文本均与仓库材料一致。IL2CPP 元数据中不含 `playback-diagnostics.json`。随包源码 SHA-256 为 `b1b6c0f64d816d0ceda408a3c3e40143176bfa0002126f502935637f31c0c2f6`。

提交前复核：再次运行完整 Unity 套件，585/585 通过；Windows PowerShell 5.1 原生构建、脚本语法及运行时文件新增/替换检查通过；从暂存区导出的干净副本（LF 文本及实际 LFS 对象）通过 8 项工具回归。CI 明确启用 LFS 拉取。源码 ZIP 的文本条目统一为 LF，避免 Windows/Linux 换行导致源码哈希误报。原始上游许可/POM 和 Unity 自动生成的 `.meta` 保持其内容与格式。

## 复现入口

先运行 `Tools/Build-Native.ps1`，同步实际库、完整源码、许可及政策。运行 `python Tools/Test-ReleaseVerification.py` 检查发布工具。Unity Test Runner 运行整个 `VRPlayer.Tests` EditMode 套件。

设备回归先执行 `python Tools/Generate-AudioDecoderFixtures.py`（需 imageio-ffmpeg），再用 Unity 随附 Java/Gradle 在 `Native/Android` 执行 `testDebugUnitTest :decoder-ffmpeg:assembleDebugAndroidTest :assembleDebugAndroidTest`。两个测试 APK 分别在 `Native/FFmpeg/decoder/build/outputs/apk/androidTest/debug/` 与 `Native/Android/build/outputs/apk/androidTest/debug/`。只选择指定的 PICO 序列号，并确认两个测试包原本不存在后安装，分别运行：

```text
adb -s <PICO_SERIAL> shell am instrument -w androidx.media3.decoder.ffmpeg.test/androidx.test.runner.AndroidJUnitRunner
adb -s <PICO_SERIAL> shell am instrument -w com.linxyi.vrplayer.test/androidx.test.runner.AndroidJUnitRunner
```

完成后仅卸载本次新增的两个测试包，验证卸载成功。不要覆盖其他人已安装的测试包。源码导出用例仅清理自己创建并得到返回 URI 的文件。

最终 APK 可用 `Tools/Verify-LegalDistribution.py --apk <APK> --manifest <apkanalyzer导出的XML>` 校验。正式签名发布入口 `Tools/Build-Release.ps1` 还会调用完整发布验证，将源码 ZIP、声明、组件清单、四份 HTML 与 APK 一同纳入 SHA256SUMS。

本次开发者称谓、主页链接与页尾调整已通过 Unity 编译、首次页及政策/许可页尾的渲染检查、8 项发布工具回归和更新后 APK 的随包文本/权限/库/源码校验。原生及设备检查沿用上表记录，原生插件未变更。

## 上架前仍需完成

本报告的编辑器检查和独立 JNI 测试不等同于最终播放器的佩戴舒适度、实体手柄交互、完整影片/多声道/TrueHD/DTS-HD 兼容性验收。尚未用本轮 APK 覆盖设备上的原播放器，也未重新签名或公开发布。

公开隐私政策 HTTPS URL 需部署 `Builds/Compliance/` 的对应 HTML 后填写到商店。付费鉴权、商店主体/地区资格及其他提审项目属于另行处理范围。软件版权分发材料不代替发行地区编解码专利权利、有效 Unity 授权或发布主体实际接受的 PICO 开发者条款。
