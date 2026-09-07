# 第三方组件与来源

本项目沿用用户已有的 Unity / PICO SDK，只新增免费或开源组件；没有付费播放器插件。

| 组件 | 固定版本 / 来源 | 许可证 / 用途 |
| --- | --- | --- |
| AndroidX Media3 | `androidx.media3:media3-exoplayer:1.9.3`，[源码](https://github.com/androidx/media) | Apache-2.0；解复用、同步、平台硬件解码、音轨与字幕轨 |
| SMBJ | `com.hierynomus:smbj:0.13.0`，[源码](https://github.com/hierynomus/smbj/tree/v0.13.0) | Apache-2.0；SMB2/3 目录和随机读取 |
| Jellyfin Media3 FFmpeg decoder | `org.jellyfin.media3:media3-ffmpeg-decoder:1.9.0+1`，[源码标签](https://github.com/jellyfin/jellyfin-androidx-media/tree/v1.9.0%2B1)，提交 `af9ee4e26b2045e3ea6f2ebf4a18ac8ebfeae396` | 上游 Maven POM 标注 GPL-3.0；补充电影音轨解码，未加入软件视频解码器 |
| Noto Sans CJK SC | [Noto CJK](https://github.com/notofonts/noto-cjk) 的 `NotoSansCJKsc-Regular.otf` | SIL Open Font License 1.1；中文 UI 与字幕。许可证与字体同目录 |
| Unity / uGUI / TMP / Input System / XRI | 项目 Packages 锁文件 | 沿用 Unity 组件各自条款；TMP Essential Resources 来自安装包 |
| PICO Unity Integration SDK | 3.4.0，提交 `c93a59bab88d337fe0d54140499712897cedbe52` | 沿用已导入 SDK 条款；设备运行、追踪与 Multiview |

Apache-2.0、Jellyfin GPL-3.0 原文及 Jellyfin Maven POM 保存在 `Docs/Licenses`。Gradle 会解析 Media3、SMBJ 的传递依赖，包括 AndroidX、Guava、Bouncy Castle、SLF4J 等，具体解析结果以构建依赖报告为准。

当前 FFmpeg 扩展采用上游现成 GPL 包，服务于已约定的个人安装使用。如果后续对外分发，需先重新评估此扩展与 Unity/PICO 分发条款的组合；可将它替换为自行构建的、仅启用所需音频解码器的适当许可 FFmpeg 扩展。此处没有为用户自有代码指定开源许可证。

`Tools/Generate-TestMedia.py` 的 imageio-ffmpeg 和 `Tools/Serve-TestSmb.py` 的 Impacket 只用于开发验证，不打入 APK；生成的测试视频不包含用户私人视频。测试服务器仅绑定 `127.0.0.1:1445`，只读暴露 `TestMedia`，通过 `adb reverse` 验证设备协议链路。

`Native/Android`、`Native/Rendering` 为本次新增的可重建桥接源码，二进制分别为 `vrplayer-android.aar` 和 `libvrplayerrender.so`。AAR 不内嵌传递依赖，Unity 的 Gradle 模板与原生工程保持相同版本。
