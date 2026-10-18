# PICO 多语言实机验收

2026-10-18，本地化功能验收完成。三语页面自动检查、设置持久化和独立的物理手柄复核通过；英语、日语佩戴可读性由用户确认。

## 设备与构建

- PICO A8110，Android 10，系统语言 `zh-Hans-CN`；Unity 6000.3.23f1，Android ARM64 / IL2CPP / GLES3 Development 构建。
- 使用独立包 `com.linxyi.LsVRPlayer.localizationtest`，显示名称为 **L's VRPlayer 验收**。正式包未被覆盖，正式版数据未改动。
- 验收 APK 为 `Tools/.runtime/ui-validation-project/Builds/LocalizationAcceptance.apk`，127090744 字节，SHA-256：`FE2DE2BC64ED694C6038C5D5F56F8B73913C6E0B85FEC91AD6BB729DD988ED12`。
- 构建在独立项目副本中执行：调用 `PlayerBuild.Configure()` 后覆盖上述包名和产品名，再以 `Development | CompressWithLz4 | DetailedBuildReport` 构建生产场景。源码与本次修改一致，仅包名和产品名用于隔离验收。

## 结果

| 检查 | 结果与范围 |
| --- | --- |
| 跟随系统 | 实际中文系统解析为中文；指定语言后可返回跟随系统 |
| 简体中文 / English / 日本語 | 通过设置面板选择，立即更新并保存；强制停止后重新启动仍保持所选语言 |
| 三语主面板 | 文件、投影、字幕选择、字幕样式、设置、播放信息通过 |
| 来源与快捷面板 | SMB 来源/发现界面、空白 Emby 登录表单、快捷模式/字幕/倍速面板通过 |
| 媒体原文与播放 | 生成的 12 秒测试片在设备后端播放；中文和日文组成的原始文件名保持原样 |
| 双语条款 | 中文时中文在前；英语、日语时英文在前，隐私/权限版本按钮顺序与默认正文正确 |
| 英文开源内容 | 开源组件及许可证英文正文在三种语言下保持一致 |
| 文字采集 | 自动页面阶段 50 份检查、2555 个标签、42 张原始双眼截图；所检查标签缺字数与截断数均为 0 |
| 真实手柄与佩戴 | 用户确认英日文字可读、语言选择及 Y 或 B 返回正常；独立复核实际保存为 English，模拟输入关闭，双手跟踪有效 |
| 编辑器回归 | 最终完整 EditMode 628 / 628 通过 |
| 恢复 | 设置、隐私同意记录、账户文件、私有命令文件按原字节或原始不存在状态恢复；临时读取权限、USB 保持唤醒设置已恢复并验证；本轮拥有的夹具及命令暂存文件已移除 |

自动页面检查使用开发版模拟指针经过共享 `VrUiInput` 路径；物理复核关闭模拟输入。两类检查分别记录，均要求佩戴感应、应用焦点和持续更新的诊断有效。

## 发现与修复

实机检查发现日语快捷字幕空列表提示过长。将其精简为 **字幕なし。SRT を選択**，保留“没有字幕、选择 SRT”的含义；新增三语真实行宽回归，重新构建并完整重跑三语页面后通过。

验收工具还修复了空文本标签采集异常，并改用 ADB 双眼截图，避免原开发版截图命令的绝对路径问题。失败时保留采集结果，便于区分工具错误与实际显示问题。

## 原始记录与复现

原始记录留在忽略目录，不提交设备备份或私有诊断：

- `Docs/Validation/LocalizationDevice/47a287e77d7b49278abda4815f65e619/`：最终 APK 的三语自动页面阶段全部完成且各语言连续健康检查通过。该次脚本整体仍保留失败状态，因为随后物理环节未读到预期英语，未将其改记为整轮成功。
- `Docs/Validation/LocalizationDevice/737e247371644a4d9a266dd939771780/`：独立物理复核通过，实际保存语言为 English、`diagnostic=false`，清理和恢复通过。可读性和 Y/B 反馈与后续实际切换观察分别保留。
- 之前的截图命令失败、空标签异常、日文截断失败记录均保留；未把中断轮次拼接成自动页面通过结果。
- 构建日志与最终测试结果在 `Docs/Validation/Localization/`，截图在对应的 `Docs/Captures/LocalizationDevice/<run>/`。

安装上述独立 Development APK，连接并授权 ADB，保持头显佩戴与播放器前台；Python 环境需 Pillow，并预先准备 `Recognition/Unmarked-A.mp4` 生成夹具：

```powershell
adb -s <PICO_SERIAL> install -r Tools/.runtime/ui-validation-project/Builds/LocalizationAcceptance.apk
python Tools/Device-LocalizationTest.py --serial <PICO_SERIAL> --manual
```

首次同意须由用户实际点击。脚本到达 `manual-ready.json` 后等待人工反馈；只有用户完成所列动作并明确确认后，才可写入同目录 `manual-answer.json`，例如 `{"passed":true,"response":"用户原始反馈"}`。脚本继续核实实际语言和物理跟踪，并严格恢复。独立物理复核可使用 `--manual-only`；它不运行三语页面套件，报告标明独立范围。

本次硬件“跟随系统”只覆盖实际中文系统，英语/日语系统匹配及其他语言回退由单元测试覆盖。SMB/Emby 仅检查本地化界面，未进行服务器兼容性或长片播放验收；短片、截图和短时佩戴确认不代表长时间舒适度或全功能稳定性。

## 经审阅的实机截图

截图仅含生成夹具与公开条款。

![英语设置](Images/Localization/PICO-English-settings.png)

![日语设置](Images/Localization/PICO-Japanese-settings.png)

![英文优先的隐私页](Images/Localization/PICO-English-privacy.png)

![日语界面的双语许可说明](Images/Localization/PICO-Japanese-licenses.png)

![修复后的日语快捷字幕空列表提示](Images/Localization/PICO-Japanese-quick-subtitles.png)
