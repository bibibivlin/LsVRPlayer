# 许可与分发范围

## 项目自有内容

本仓库的项目自有代码与文档采用 [MIT License](../LICENSE)，版权归相应作者与贡献者。MIT 允许使用、修改与再分发，并要求保留版权及许可声明；完整条款以英文许可证为准。参见 [MIT 官方文本](https://opensource.org/license/mit)。

该授权适用于项目自行编写的 C#、Java、C++、着色器、开发脚本与文档等内容，不改变已经标注上游来源或独立条款的内容。

## 第三方内容

第三方组件、SDK、字体、导入资源及其二进制保留原有许可证和版权。`Assets/` 或 `Packages/` 下的内容不能仅凭所在目录视为 MIT。组件清单见 [第三方声明](ThirdPartyNotices.md)，包括：

- Media3、SMBJ、SMBJ-RPC 等组件及传递依赖。
- Noto 中文字体、TMP 资源和其他上游导入素材。
- Unity 与 PICO SDK 及其各自的使用和分发条款。
- 可重建 FFmpeg 8.1.3 共享音频库的 LGPL-2.1-or-later 许可及 Apache-2.0 的 Media3 1.9.3 音频封装。

再分发时需要保留适用的许可证与声明；只有第三方链接或根目录 MIT 文件，不代表所有组件的义务都已履行。

## 新构建的音频解码方案

新构建已移除 `org.jellyfin.media3:media3-ffmpeg-decoder:1.9.0+1` GPL 预编译包，改用可重建的 FFmpeg **8.1.3** LGPL-2.1-or-later 共享音频库，以及 Media3 **1.9.3** Apache-2.0 音频封装。

- 配置关闭 GPL、nonfree、version3 和外部编解码库，只启用所列音频解码器，不启用软件视频解码器。
- `libavcodec.so`、`libavutil.so`、`libswresample.so` 独立共享链接；Apache JNI 封装为 `libffmpegJNI.so`，不嵌入 FFmpeg 静态库。
- [源码锁定记录](../Native/FFmpeg/sources.lock.json) 固定 FFmpeg 完整源码包、Media3 上游提交和每个实际封装源码文件的 SHA-256。[构建记录](../Native/FFmpeg/binary-manifest.json) 固定实际库与对应源码归档的 SHA-256。
- APK 的 `assets/Legal/ffmpeg-lgpl-corresponding-source.zip` 随包携带完整 FFmpeg 源码、实际 Media3 Java/JNI 源码、许可及构建脚本。信息 → 第三方许可 → 导出源码可保存到 Android Download，也可直接解压 APK 提取。
- 应用内离线第三方声明保留原始许可和版权，并说明修改 LGPL 库、为调试这些修改进行必要逆向工程的权利。共享库替换、重建及重新签名说明见 [音频库 README](../Native/FFmpeg/README.md)。项目自有条款不限制这些权利；Unity/PICO 仍沿用其各自条款。

原先已经构建或发布的 APK **不会被本次源码更新改变**。历史 Jellyfin GPL 文本与 POM 继续保留供追溯，不能用作新库的许可材料，也不能把旧二进制改称 LGPL。

## 分发材料与校验

`Tools/Build-Native.ps1` 重建音频库、Java 桥和渲染插件，同时解析实际 Android 运行时依赖，保存原始 POM、随依赖提供的 LICENSE/NOTICE、必要的 MIT/BSD 版权文本、字体及 Unity/PICO 声明。完整清单见 [RuntimeComponents.json](Licenses/RuntimeComponents.json)，可读声明见 [THIRD-PARTY-NOTICES.txt](Licenses/THIRD-PARTY-NOTICES.txt)。

`Tools/Verify-Release.py` 对最终 APK 核对实际权限、备份配置、四个音频库哈希、随包完整源码及构建脚本、离线政策/权限/许可文本和组件清单；缺失或过期即阻止发布。签名发布输出还附带对应源码 ZIP、完整声明、组件清单和中英文隐私/权限 HTML，全部进入 SHA256SUMS。公开发布流程将这些材料与 APK 一起上传。

这些检查解决已识别的软件版权分发材料问题，不代表所有商业法律条件已自动满足。Unity/PICO 商业使用与分发条款仍需由发布者遵守。[FFmpeg 官方说明](https://ffmpeg.org/legal.html) 区分软件版权许可与编解码专利；中国大陆及海外付费发行涉及的音频专利权利须按发行地区另行确认，不能仅凭 LGPL 宣称已取得所有商业编解码授权。

PICO SDK 随包 LICENSE 为专有版权声明，不是开源授权。保留声明不代表取得 SDK 的独立再分发许可；发布主体还需遵守实际接受的 [中国大陆开发者协议](https://developer-cn.picoxr.com/terms/) 与 [海外开发者条款](https://developer.picoxr.com/terms/)，并核对其账户/SDK 使用条件。Unity 同样适用发布主体的有效授权。本次没有代用户接受平台协议或确认其账户商业权利。

## 新增或更新依赖

PR 应说明上游来源、固定版本、许可证、是否打入运行时及需要保留的材料。同步更新 [第三方声明](ThirdPartyNotices.md) 和所需许可文本；原生与 Unity Gradle 版本必须保持一致。
