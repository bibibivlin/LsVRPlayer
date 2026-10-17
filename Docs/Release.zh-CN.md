# 正式版本构建与发布

版本来源是 `ProjectSettings/ProjectSettings.asset` 的 `bundleVersion` 与 `AndroidBundleVersionCode`。1.0 正式版为 **1.0.0 / 7**；Configure 保留版本，不再重设为开发版本。包名保持 `com.linxyi.LsVRPlayer`。

## 本地构建

需要 Windows、Unity **6000.3.23f1**（有效本机授权和 Android SDK／NDK／OpenJDK）、Git LFS、Python **3.11+**。在仓库根目录执行：

```powershell
./Tools/Build-Release.ps1 -Stage Build
# 在当前进程安全设置 ANDROID_KEYSTORE_PASS、ANDROID_KEYALIAS_NAME、ANDROID_KEYALIAS_PASS 后：
./Tools/Build-Release.ps1 -Stage Sign -KeystorePath <仓库外的密钥库完整路径>
```

密码只通过环境传递，不能填写在脚本、命令参数、版本控制文件或日志中。也可提供 `ANDROID_KEYSTORE_BASE64` 并省略 `-KeystorePath`。`-Stage All` 顺序执行构建与签名。Unity 安装路径不同时传入 `-UnityEditor <Editor目录>`。

Build 阶段重建 Java AAR／ARM64 GLES 插件，运行发布工具回归、原生 JUnit 和完整 `VRPlayer.Tests` EditMode 套件，再以 ARM64／IL2CPP Release／GLES3 构建非 Development Player。中间 APK 使用开发签名，仅保存在忽略的 `Builds/.release-staging/`，不得分发。

EditMode 套件包含实际图像渲染断言，因此测试使用本机 Direct3D 11 图形设备，不启用 `-nographics`；不跳过这些测试。Android Player 编译仍使用无图形批处理模式。

Sign 阶段验证所选证书有效期，使用 `apksigner` 重新签名，并核对证书、包名、版本、SDK、ARM64 库、`debuggable=false`、`testOnly=false` 和通过的测试记录。只有全部成功，`Builds/Release/` 才保留最终 APK、清单、SHA-256 与发布说明。签名临时目录在成功和失败时均清理；失败不保留可发布输出。原始 Unity 日志和测试 XML 保留在忽略的 `Docs/Validation/release/`。

## GitHub Actions

`Signed Android Release` 使用仓库专属 Windows 自托管 Runner，标签为 `self-hosted, Windows, X64, vrplayer-release`。GitHub 调度构建，本机运行 Unity，发布步骤在 GitHub 托管 Ubuntu Runner 执行。构建期间本机必须在线、Runner 运行；无需把 Unity 账户密码或本机授权文件上传到 GitHub。

Runner 由仓库 Settings → Actions → Runners 注册，使用已授权用户身份运行。不得在公开 PR 或不可信分支执行签名工作。此流程只自动构建 `codex/release-*` 分支和 `v*` 标签，也支持手动运行；普通 PR 只在 GitHub 托管环境执行无签名资料的工具检查。

Runner 安装在当前用户的 `%LOCALAPPDATA%/LsVRPlayer/actions-runner`。本次注册后已后台启动；电脑重启后，在项目根目录执行 `./Tools/Start-ReleaseRunner.ps1` 重新启动。脚本会检测现有进程，避免重复运行。构建脚本使用 Python 的真实可执行文件；需要指定安装时可设置 `VRPLAYER_PYTHON`。

在仓库 Settings → Secrets and variables → Actions 设置：

| Secret | 内容 |
|---|---|
| `ANDROID_KEYSTORE_BASE64` | 发布密钥库文件的 Base64 编码 |
| `ANDROID_KEYSTORE_PASS` | 密钥库密码 |
| `ANDROID_KEYALIAS_NAME` | 私钥 Alias |
| `ANDROID_KEYALIAS_PASS` | 私钥密码 |

可设置变量 `VRPLAYER_SIGNING_CERT_SHA256` 锁定发布证书指纹。首次可信签名后，从发布清单读取该公开指纹；后续密钥变化将导致失败。

第三方 Actions 固定到提交 SHA。签名资料仅注入签名步骤，不传入 Unity 进程。GitHub artifact 只上传验证完成的发布文件，不上传密钥、Unity 日志、私有设置或原始测试记录。

## 发布步骤

1. 更新 Version 和递增 Version Code，添加 `Docs/Releases/v<版本>.zh-CN.md`。
2. 提交 `codex/release-*` 分支并创建 PR；工具检查与实际签名构建通过后合并至 main。
3. 给合并提交添加与 Version 完全一致的标签（例如 `v1.0.0`）并推送。
4. 标签工作流再次运行完整构建、测试和签名。仅已合并到 main 的提交允许发布。
5. 发布任务检查 artifact 的 SHA-256，再创建 GitHub Release 草稿、上传 APK／校验值／清单并发布为 Latest。失败时不发布成品，不覆盖既有 Release。

`VRPlayer → Build Android APK` 仍保留开发包用途。正式包没有开发命令入口，设备自动化脚本不能替代最终正式包的手动／佩戴验收。既有开发包与新发布包跨签名安装前应备份数据。
