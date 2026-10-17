# 贡献指南

欢迎提交问题、修复、文档和功能建议。参与讨论请遵守 [行为准则](CODE_OF_CONDUCT.md)；安全漏洞请按 [安全政策](SECURITY.md) 报告。

## 提交问题

先搜索已有 Issue，并查看 [使用说明](Docs/BuildAndUse.zh-CN.md) 和 [已知限制](README.md#已知限制)。报告问题时提供：

- 应用版本、头显型号与系统版本。
- 本地 / SMB / Emby 来源类型、复现步骤、预期与实际结果。
- 涉及播放时的视频容器、编码、分辨率、帧率和字幕类型，能够确认多少就填写多少。
- 必要的脱敏日志或截图；不要上传密码、令牌、服务器地址、私人媒体、设备备份或签名资料。

功能建议请说明使用场景。较大的架构或依赖变更，建议先通过 Issue 讨论范围。

## 开发环境

使用 Unity **6000.3.23f1**、Android Build Support（SDK、NDK、OpenJDK）和 Git LFS。构建命令、原生测试与设备准备见 [构建说明](Docs/BuildAndUse.zh-CN.md)，签名发布见 [发布指南](Docs/Release.zh-CN.md)。

修改前阅读 [AGENTS.md](AGENTS.md) 中的项目约定。主要目录：

| 目录 | 内容 |
| --- | --- |
| `Assets/VRPlayer/Runtime/` | 播放、文件来源、字幕、持久化与界面 |
| `Assets/VRPlayer/Editor/` | 项目配置和构建工具 |
| `Assets/VRPlayer/Tests/` | Unity 测试 |
| `Native/Android/`、`Native/Rendering/` | Android 桥和 GLES 插件 |
| `Tools/`、`Docs/` | 开发工具与文档 |

## 提交修改

1. 每个 PR 聚焦一个问题，说明修改前后的行为；避免无关重排。
2. 遵循附近代码风格：四空格缩进，C# 类型 / 方法使用 PascalCase，字段 / 局部变量使用 camelCase。
3. Unity 资源与 `.meta` 一起提交，保留现有 GUID；原生代码变更后重建所需 AAR / SO，二进制沿用 Git LFS。
4. 为解析、时间、持久化、定位或取消行为变更补充回归。运行相关测试并记录结果；纯文档改动检查链接与格式即可。
5. UI 变更附脱敏截图。原生 / 渲染变更需要 PICO 实机验证；无法执行的检查如实注明，不用编辑器预览或短样片代替长片与佩戴验收。

通常从 Unity Test Runner 运行 `VRPlayer.Tests` EditMode 测试；涉及 UI 运行行为时执行相关 PlayMode 测试。设备脚本会使用开发 APK，运行范围、数据备份和恢复要求以对应指南为准。

提交前检查差异，不提交 `Library/`、构建产物、测试媒体、设备原始记录或私密数据。不要在外部 PR 中运行需要发布密钥或自托管 Runner 的工作流。

## 贡献许可

提交贡献表示你有权提供该内容，并同意将项目自有部分的贡献按 [MIT](LICENSE) 发布。引入第三方代码或资源时保留其原许可证，并更新 [第三方声明](Docs/ThirdPartyNotices.md)；依赖许可与 APK 分发范围见 [许可说明](Docs/Licensing.zh-CN.md)。
