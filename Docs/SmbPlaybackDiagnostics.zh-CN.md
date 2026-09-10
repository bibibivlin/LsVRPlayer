# SMB 播放预读与缓冲诊断

实际记录、设备限制与验收状态见 [修复验证报告](SmbPlaybackValidation.zh-CN.md)。

## 修复内容

SMB 播放数据源原先将解封装的小块读取逐次发送到服务器。MP4 NAL 长度、音频和视频负载读取都可能承担独立网络往返，实际供给速度因请求延迟下降，即使低码率 AVC 也可能反复缓冲。

现在每个 SMB 播放数据源使用可复用的 1 MiB 字节缓存。缓存未命中时在 Media3 加载线程按需填充，后续小读取直接从内存返回；没有持续预读线程或额外视频解码。请求受服务器协商上限、文件长度和 DataSpec 范围限制，接受正数短读，提前 EOF 和零长度响应作为读取错误。

新范围使旧缓存失效。每次影片打开创建独立会话，数据源工厂捕获该会话；切片、停止和释放会取消旧连接，包括仍在建立的连接。元数据探测继续使用独立资源，旧请求无法计入新影片的网络统计。

预读后的实测还发现，原先共用的 15 秒 socket 超时会使 SMBJ 后台收包线程在播放器缓冲充足、暂不读取时退出，下一轮加载需要重连。播放连接现使用 `SO_TIMEOUT=0` 保留空闲连接，同时保留 15 秒连接建立与 SMB 请求期限；切片取消仍主动关闭 socket。目录、字幕和探测的 socket 超时保持原设置。其依据是 SMBJ 的 [DirectTcpTransport](https://github.com/hierynomus/smbj/blob/v0.13.0/src/main/java/com/hierynomus/smbj/transport/tcp/direct/DirectTcpTransport.java) / [PacketReader](https://github.com/hierynomus/smbj/blob/v0.13.0/src/main/java/com/hierynomus/smbj/transport/PacketReader.java) 实现以及独立的 [SmbConfig 请求超时](https://github.com/hierynomus/smbj/blob/v0.13.0/src/main/java/com/hierynomus/smbj/SmbConfig.java)。

缓冲策略保持 12/45 秒、首播 0.8 秒、重缓冲恢复 2.5 秒、64 MiB 大小优先。64 MiB 是样本队列大小目标，不是进程总内存上限，也不是 SMB 预读缓存大小。

## 信息页和日志

信息页增加剩余缓冲、加载状态、样本队列分配量、意外缓冲次数/时长，以及 SMB 读速、请求数、平均请求大小和读取延迟。

`MediaSnapshot` 新增 `monotonicMs`、`playWhenReady`、`suppressionReason`、`loading`、`bufferAheadMs`、`allocatedBufferBytes`、`bufferReason`、`rebufferCount`、`rebufferMs` 和 `network`。旧消费者可以忽略新增字段。

| 网络字段 | 含义 |
|---|---|
| `generation` / `monotonicMs` | 所属影片会话 / 设备单调时钟，毫秒 |
| `readAhead` | 本次打开是否启用预读 |
| `requests` / `smallReads` | 实际 SMB 文件读取调用数 / 请求不超过 4 KiB 的调用数 |
| `networkBytes` / `deliveredBytes` | 从 SMB 收到的字节 / 交给解封装器的字节；缓存命中不重复计入网络流量 |
| `mbps` | 最近约 1 秒墙钟窗口的网络读速；缓存已满、暂停加载时可为 0 |
| `averageRequestBytes` / `averageReadBytes` | 本会话平均请求大小 / 实际返回大小 |
| `averageReadMs` / `readP95Ms` | 本会话平均读延迟 / 最近最多 256 次请求的 P95 延迟 |
| `errors` / `cancelledReads` / `opens` | 网络读取失败 / 因跳转或切片取消的读取 / 成功打开范围次数；不包括元数据探测、目录或字幕连接 |

`allocatedBufferBytes` 是 Media3 allocator 的实际分配字节数；不包括解码器、Unity 纹理和 SMB 缓存。`bufferAheadMs` 使用 Media3 总缓冲时长，不以文件总长代替可播放余量。

应用外部文件目录下的 `playback-events.jsonl` 后台写入约每秒一次的状态样本，以及事件驱动的 `buffer-start` / `buffer-end`、`seek`、`transition`、`play-intent`。区分 `initial`、`seek`、`transition`、`paused`、`suppressed` 与 `rebuffer`；只有最后一种累加意外缓冲。暂停或跳转会结束当前意外缓冲区间。文件达到约 8 MiB 时轮换到 `playback-events.previous.jsonl`，保留当前和上一份。

事件队列上限为 256，溢出计数写入 `droppedDiagnosticEvents`。日志只包含匿名会话、时间和性能计数，不包含媒体 URI、文件名或 SMB 凭据。状态事件也使用 `VRPlayerPlayback` logcat 标签。原有 `playback-diagnostics.json` 仍约每 2 秒更新，因此不能用其单独排除短暂缓冲。

## 复现与对照

保留原 APK 后重建原生插件和开发 APK。开发命令 `smb-read-ahead` 的 `flag` 控制**下一次打开**的预读，默认开启、不持久化，非 debuggable APK 拒绝改变。`flag=false` 用于“诊断开启、预读关闭”对照。

在被忽略的 `Docs/Validation` 中准备 cases JSON 数组，`id` 使用匿名字母数字标识；媒体只来自明确指定的样本或项目生成素材：

```json
[
  {"id":"AVC_SMB","uri":"smb://配置ID/样本.mp4","startMs":60000},
  {"id":"AVC_LOCAL","uri":"file:///sdcard/Movies/VRPlayerTest/SMB.AVC.Sample01.mp4","startMs":60000},
  {"id":"HEVC_LOCAL","uri":"file:///sdcard/Movies/VRPlayerTest/Performance.8K30.HEVC.mp4","startMs":0,"loop":true}
]
```

```powershell
# 先安装保存的原版 APK，使用同一设备、网络和 cases。
python Tools/Device-SmbPlaybackTest.py --serial <PICO_SERIAL> --cases Docs/Validation/private-cases.json --mode baseline --seconds 600
# 安装修复版开发 APK 后：
python Tools/Device-SmbPlaybackTest.py --serial <PICO_SERIAL> --cases Docs/Validation/private-cases.json --mode diagnostics --seconds 600
python Tools/Device-SmbPlaybackTest.py --serial <PICO_SERIAL> --cases Docs/Validation/private-cases.json --mode read-ahead --seconds 600
# 60 分钟验证使用单个足够长的样本，或明确标记循环边界：
python Tools/Device-SmbPlaybackTest.py --serial <PICO_SERIAL> --cases Docs/Validation/long-case.json --mode read-ahead --seconds 3600 --output Docs/Validation/smb-long.json
```

工具固定 1×速度、平面 Mono、高清画质并关闭自动识别和连播；备份并逐字节验证恢复原设置，同时临时启用供电时保持唤醒并在退出时恢复。PICO 的屏幕超时可能为 Android shell 无法写回的特殊值 `-1`，工具不修改该设置。播放器必须保持前台；如果固件仍因未佩戴而暂停，需要佩戴后再验证。外部暂停、失焦或诊断停止更新会使该轮失败，不会计为通过。原版没有事件计数，其轮询只能记录采样时看到的缓冲。

先比较同一 AVC 的本地和实际 NAS，再比较诊断模式与预读模式下的请求大小、请求数、缓冲时长和内存曲线。生成 HEVC 短片循环只验证路径回归，不能替代独立真实 HEVC 长片与主观佩戴验收。ADB reverse 仅用于协议回归，不作为实际 Wi-Fi/NAS 吞吐证据。

## 自动回归

Native JUnit 覆盖连续小读合并、跨块与短读、限定范围、超过 4 GiB 偏移、EOF、取消中的读取、重新打开、统计隔离和事件分类。命令见 `BuildAndUse.zh-CN.md`。Unity EditMode suite 验证现有播放、字幕、设置和格式识别行为。

修改原生代码后运行 `Tools/Build-Native.ps1` 并构建 APK；保留更新的 `Assets/Plugins/Android/vrplayer-android.aar`，沿用原 `.meta` GUID 与 Git LFS 规则。测试样本、原始报告、设置备份和 APK 保持忽略。
