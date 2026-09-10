# AVC 经 SMB 播放修复验证（2026-09-10）

## 结论与范围

真实 NAS 上的问题 AVC 已复现出读取供给不足：仅增加诊断、关闭预读时，约 197 秒墙钟时间只前进 141.45 秒，记录到 18 次意外缓冲、累计 53.919 秒。平均 SMB 请求仅 2,527 字节，每秒约 310 次；约 84% 请求不超过 4 KiB。吞吐约 6.27 Mbps，低于该片视频加音频约 8.70 Mbps 的消耗。

同时观察到剩余缓冲最低 46 ms，实际样本队列分配量最高仅 3.75 MiB，Unity 帧率中位数约 71.8 fps，视频解码丢帧为 0。因此，这条样本的证据支持小请求和往返延迟导致停供；64 MiB 大小目标提前停止加载不能解释这一轮卡顿。

加入预读后的一轮真实 NAS 播放持续约 290 秒，媒体时间前进约 289 秒，未记录到意外缓冲或解码丢帧；请求下降到约 1.19 次/秒，平均 1 MiB，没有小请求。头显随后自动暂停，这轮不能计为 10 分钟验收通过。

该轮预读同时暴露了正常加载间歇触发 15 秒 socket 空闲超时的问题，共有 8 次读取失败后重开范围。最终代码只对播放连接取消 socket 空闲超时，保留 15 秒请求期限与主动关闭取消。实现及指标定义见 [SMB 播放预读与诊断](SmbPlaybackDiagnostics.zh-CN.md)。

## 环境与样本

- Unity 6000.3.23f1，Android ARM64 开发 APK，Media3 1.9.3，SMBJ 0.13.0。
- PICO 真机通过实际局域网访问用户指定 NAS；没有使用 ADB reverse 代替真实 SMB 吞吐。
- 匿名样本 `AVC_SMB` / `AVC_LOCAL` 是同一文件：2160×1080、59.94 fps、AVC Constrained Baseline，视频约 8.51 Mbps，AAC 约 0.189 Mbps；时长约 30 分 27.56 秒。
- 固定从 60 秒处开始、1×速度、平面 Mono、高清画质，关闭自动识别、自动字幕与连播。硬件视频解码器为 `OMX.qcom.video.decoder.avc`。
- HEVC / 8K 回归使用项目生成素材；未获得独立真实 HEVC 对照长片。文件路径、文件名、凭据、设置备份和原始截图不纳入本报告。

## 已保存记录

原始文件均位于被忽略的 `Docs/Validation/`。JSONL 状态事件由设备单调时钟与 generation 关联；轮询诊断约每 2 秒更新，不能单靠它排除短暂缓冲。

| 记录 | 实际覆盖 | 结果与限制 |
|---|---|---|
| `smb-baseline-valid.json` | 原 APK，约 309 秒真实 SMB | 媒体前进约 215.7 秒，反复观察到缓冲；随后头显自动暂停。原版没有事件计数 |
| `smb-diagnostics-partial.json` | 仅诊断，约 197 秒真实 SMB | 18 次意外缓冲，53.919 秒；测试读取诊断文件碰到重写而结束，已修复脚本短暂重试 |
| `smb-read-ahead-short.json` | 早期预读版，同片 SMB / 本地各 60 秒 | 均无意外缓冲；旧版本未区分取消与读取错误，且清理阶段报告未最终保存，不能作为完整验收 |
| `smb-read-ahead-10min.json` | 预读版，约 290 秒真实 SMB | 0 次意外缓冲；空闲超时导致重连，随后头显自动暂停，未满 10 分钟 |

最终构建（包含播放连接空闲超时修正）的实际 SMB 检查完成 180.31 秒，记录为 `smb-final-network.json`：

| 指标 | 最终构建 |
|---|---|
| 意外缓冲 / 解码丢帧 / 网络读取错误 | 均为 0 |
| 网络请求 | 测量窗口内 225 次，约 1.25 次/秒；平均 1 MiB，小请求 0 次 |
| 打开范围次数 | 测量开始及结束均为 3，播放期间没有重新打开连接 |
| 剩余缓冲 | 最低 12.314 秒，中位数 29.13 秒 |
| 样本队列分配量 | 最高 48.44 MiB |
| 进程 PSS | 685.1–688.5 MiB；首次 686.7 MiB、末次 687.7 MiB |
| 设置恢复 | 播放器设置逐字节核验通过；USB 保持唤醒设置恢复通过 |

APK SHA-256：`435ff8b3e4492417159b5d783712b86c5f4487119adbc43c4313e6df3332d769`。AAR SHA-256：`64f9dde8852aff55bf55a716f712eb53f72b85611be146779016fc60b17213b6`。播放器保留原 12/45 秒、0.8/2.5 秒及 64 MiB 缓冲策略，因此上述改善不依赖增大 Media3 缓冲阈值。

同一 AVC 的最终版本地对照完成 60 秒，0 次意外缓冲、0 解码丢帧，见 `smb-final-local.json`。短时回归还完成 8 次 SMB / 本地交替打开；最终本地会话的网络请求计数为 0，旧会话没有污染新会话统计。

`Device-StabilityTest.py --cycles 18` 通过：生成的 4K60 / 8K30 HEVC 正常输出，18 轮打开、分辨率切换及 seek 完成；预热后 PSS 从约 999.9 MiB 降到 895.2 MiB。8K 色条画面已截图复核。`Device-CaptionTest.py` 的字幕载入、距离和样式命令通过，完成 9 组截图；抽查 3 米字幕截图中中英文均正常显示。记录分别保留在 `device-stability.json` 和 `caption-device.json`，测试外层另行备份并恢复原设置。

`Device-AdjacentPlaybackTest.py` 的 8 项相邻播放、格式继承、重新识别和撤销检查全部通过。外层汇总 `smb-device-regression.json` 确认所有短时回归完成，播放器与供电设置恢复核验通过。

## 自动检查

- Native JUnit：41 / 41 通过，包括新增的 8 项读取缓存测试和 9 项诊断 / 会话测试。覆盖短读、跨块、范围末尾、超过 4 GiB、取消、重新打开以及事件分类。
- Unity EditMode：138 / 138 通过，Console 无错误。原生 AAR 与 APK 已重新构建。
- 信息页以项目实际中文字体、25 号字和 1376×590 面板测量，代表性长计数字段（含两行画质信息）需要约 579 像素高度，能完整放入。此检查不替代主观头显可读性验收。

最终信息页已在生成的本地 AVC 素材上截图复核，文字与新增缓冲指标正常显示。该截图没有 SMB 网络两行，完整 SMB 布局另以上述尺寸测量覆盖。

![生成素材上的信息页](Images/smb-info-generated.png)

## 复现命令

```powershell
./Tools/Build-Native.ps1
# 在 Unity 中构建 Android 开发 APK，或使用项目既有构建工具。
adb -s <PICO_SERIAL> install -r Builds/LsVRPlayer.apk
python Tools/Device-SmbPlaybackTest.py --serial <PICO_SERIAL> --cases Docs/Validation/private-cases.json --mode diagnostics --seconds 600 --output Docs/Validation/diagnostics-10min.json
python Tools/Device-SmbPlaybackTest.py --serial <PICO_SERIAL> --cases Docs/Validation/private-cases.json --mode read-ahead --seconds 600 --output Docs/Validation/read-ahead-10min.json
```

保留原版 APK，使用 `--mode baseline` 重跑第一组。最终 60 分钟检查应单独执行 `--seconds 3600`；短片或不足 60 分钟影片需要在 cases 中显式设置 `loop:true`，并标记循环边界。播放器必须持续在前台且头显不能因未佩戴自动暂停。

## 设备设置恢复与剩余验收

各轮新脚本均备份并逐字节核验恢复播放器设置，供电保持唤醒设置恢复为原值 `0`。旧的 60 秒测试曾把 PICO `screen_off_timeout` 从固件特殊值 `-1` 改成 `7200000`（2 小时）；Android SettingsProvider 拒绝写回 `-1`，官方企业接口在这台固件上不可用。临时恢复工具已卸载，未修改企业服务状态。**这项息屏超时目前仍是 2 小时，不能声称设备全部设置已恢复。** 新脚本不再修改它；后续需要在设备设置界面恢复原息屏选项。

播放器设置备份实际恢复后的 SHA-256 已与原始备份一致。旧短测报告中的 `settingsRestored:false` 来自清理异常前保存的旧状态，原始记录保留不改写。

由于头显在无人操作时约 5 分钟自动暂停，完整三组 10 分钟对照、60 分钟连续播放和持续内存增长验收仍未完成。短时 PSS 波动不能证明没有长期泄漏；生成 HEVC、截图检查也不能替代独立长片与主观佩戴验收。
