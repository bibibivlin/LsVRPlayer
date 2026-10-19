# 主面板点击播放后自动关闭：实机验证

2026-10-19（Asia/Shanghai），USB PICO 4（A8110），Unity 6000.3.23f1，Android ARM64／IL2CPP Development。本次验证主面板底部播放／暂停按钮：点击播放立即收起主面板，点击暂停保留主面板。

## 结果与范围

实机自动化 **13／13 通过**，包括准备和恢复共 86.51 秒。21:14:31—21:15:45 的 38 次诊断更新均保持应用焦点、已知且有效的头显佩戴状态；连续监测没有发现诊断过期或状态中断。测试通过开发版 `ui-pointer` 入口进入共享 `VrUiInput.Dispatch` 和真实 uGUI 按钮回调，使用已生成的本地 12 秒短片 `Recognition/Unmarked-A.mp4`。

| 检查 | 数量 | 结果 |
| --- | ---: | --- |
| 未打开影片时点击禁用的播放按钮，主面板保持显示 | 1 | 通过 |
| 关闭自动隐藏，在文件／投影／字幕／设置／信息页点击播放，面板收起且后端播放 | 5 | 通过 |
| 播放中重新打开上述各页，点击暂停后面板保持显示且后端暂停 | 5 | 通过 |
| 启用自动隐藏时点击播放，主面板同样收起 | 1 | 通过 |
| 快捷面板点击播放及暂停，快捷面板均保持显示 | 1 | 通过 |

另有编辑器 `QuickControlsTests` 111／111 通过，以及播放、暂停、无影片、无后端、快捷面板五项按钮回调检查。构建后 Unity Console 无错误。

本次使用模拟指针的真机功能检查；**物理扳机操作、可读性与佩戴舒适度尚未人工验收**。没有测试真实 SMB／SAF／Emby、长影片或其他格式的兼容性。

## 构建与设备恢复

实际安装并测试的是独立包 `com.linxyi.LsVRPlayer.MainPanelAutoClose`，使用同一份 Runtime 代码和生产场景。验证包构建 32.60 秒，0 错误、11 条警告；APK 为 `Builds/LsVRPlayer-MainPanelAutoClose-validation.apk`，186,758,254 字节，SHA-256：

```text
4547a7ae7b820ffa9c0ccfc896216758b808cf72e9a6b6acd101404701a53750
```

设备安装文件与本地 APK 的 SHA-256 一致。原正式版没有覆盖安装，原 APK 的设备哈希与测试前备份一致。验证应用复用设备上现有、版本相符的产品隐私同意记录；原记录已核对保持原字节。

结束前 force-stop 验证应用，逐项核对 10 个设置、账户、隐私、诊断及命令文件恢复原字节或原不存在状态，移除经过所有权核验的命令暂存目录；本轮 10 个文件原先均不存在，均已恢复不存在。临时验证应用随后卸载，原正式版保留。没有改动 `screen_off_timeout` 或 USB 常亮设置，结束时再次核对两者原值。构建写回的字体与项目设置变化已恢复，没有修改原生插件。

## 复现与原始记录

生成短片的命令为 `python Tools/Generate-RecognitionMedia.py --clips`，将生成的 Recognition 目录复制到头显 `/sdcard/Movies/VRPlayerTest/Recognition/`。使用 Android Development 构建安装到独立验证包，在当前隐私同意有效、头显唤醒且播放器前台时执行以下步骤：

1. 无影片时尝试点击播放，确认主面板保持显示。
2. 打开生成短片，关闭设置中的自动隐藏，暂停播放。
3. 依次在五个主面板页签中点击底部播放，确认面板收起、视频继续；重新打开面板点击暂停，确认面板保持显示。
4. 启用自动隐藏，再检查一次点击播放收起行为。
5. 在快捷面板中分别点击播放和暂停，确认快捷面板保持显示。

本机一次性自动化脚本及完整结果保存在忽略目录，重新运行需先安装上述验证包：

```powershell
python Docs/Validation/main-panel-autoclose-20261019-ae42b1/run-device.py --serial <PICO_SERIAL>
```

该目录还保留 `report.json`、`summary.json`、`presence-and-freshness.jsonl`、`command-results.jsonl`、`apk-verification.json`、构建日志和设备日志。四张原始双眼截图位于忽略的 `Docs/Captures/main-panel-autoclose-20261019-ae42b1/`，均已查看。以下两张选图仅含生成短片与无账户的验证界面，已检查无私密媒体或账户信息：

点击播放前，主面板可见：

![播放前主面板可见](Images/MainPanelAutoClose-Before.png)

点击播放后，主面板收起，生成短片继续显示：

![播放后主面板收起](Images/MainPanelAutoClose-After.png)
