# Emby 背景、封面与首页排版调整

2026-10-15。延续播放器 A 方案的深色渐变与青绿色状态色，调整 Emby 浏览界面。

- 移除 Emby 独立的不透明背景，直接显示播放器共用的页面背景；透明输入面仍保留，空白区域的指针行为不变。
- 媒体库封面改为 16:9，列宽根据可用区域计算。普通封面和缩略图等比放大并居中裁切，填满圆角画框；Disc、Logo 继续完整显示并保留 PNG 透明度。
- 继续播放统一为 16:9 横向缩略图，优先请求 Thumb，缺失时使用 Backdrop，再缺失时才使用 Primary；最后一种仍保持横向画框。当前带来源栏的窗口每行 5 项，两端对齐，不再为固定的旧卡宽留出大段空位。剧集标题与季集信息分两行显示。
- 最新加入的海报列也按可用宽度计算；媒体库入口、继续播放和海报墙使用一致的左右对齐线。
- 服务器与账户列表删除左侧竖线，当前账户使用共用的青绿色底色、轮廓和标题高亮，移除按钮仍保持独立。
- 普通影片的播放按钮宽度从 416 缩为 248，续播为 320；保留 56 的点击高度。分段续播按钮保留较宽空间来显示 CD 与时间，播放和续播动作仍走原路径。

## 原生预览

以下为实际 uGUI 渲染，图片使用标注类型与比例的合成纹理，便于检查画框是否铺满；不是 AI 概念图。覆盖媒体库、8 项继续播放、账户列表、普通影片和分段详情。

| 首页 | 继续播放 |
| --- | --- |
| ![首页](Images/EmbyUIRefinement/24-EmbyHome.png) | ![继续播放](Images/EmbyUIRefinement/25-EmbyResume.png) |

| 账户高亮 | 播放按钮 |
| --- | --- |
| ![账户高亮](Images/EmbyUIRefinement/26-EmbyAccounts.png) | ![播放按钮](Images/EmbyUIRefinement/27-EmbyPlayButton.png) |

预览通过独立 Unity 工程快照运行 `VRPlayer.Editor.UIRedesignPreview.ExportBatch -uiArtworkFixtures`，共导出 27 张原生界面。测试纹理不会加入运行时资源或 APK。

## 验证

Unity 6000.3.23f1。EditMode 549 / 549、PlayMode 6 / 6 通过，C# 编译无错误。新增回归覆盖当前窗口每行 5 张横图、7 种视图的等比缩放与透明图完整性、图片卸载后占位恢复，以及账户高亮与透明页面背景。

PICO A8110 合成服务回归 4 / 4 通过，250 次焦点、佩戴与新鲜诊断采样有效，保存 8 张设备截图。实际验证了带认证头的 Thumb / Backdrop 请求、横向媒体库封面、账户返回、完整 205 项影片墙、海报 / 缩略图 / Disc / Logo 视图切换，以及缩小播放按钮启动原有后端。设备四份账户及设置文件逐字节或按原始不存在状态恢复，服务 UserData 与会话恢复、临时命令和端口映射清理均通过；USB 保持唤醒值没有改变，未修改屏幕关闭超时。已重新启动正常播放器。

最终 Development APK 为 `Builds/LsVRPlayer.apk`，145,515,276 字节；已安装文件与本地 SHA-256 一致：

```text
cf306d4b12ccc1b22d45ee437d7f7ef6b08d7f5b9ddb36943afc18fe56e0faba
```

本轮专用设备驱动与原始记录同属忽略目录，复核命令为：

```powershell
python Tools/.runtime/emby-ui-refinement/Device-EmbyUILayout.py --serial <PICO_SERIAL> --adb <ANDROID_SDK_ADB> --media TestMedia/Recognition/Unmarked-A.mp4 --duration-ms 12000
```

原始记录保存在忽略目录 `Docs/Validation/EmbyUIRefinement/`。设备验证使用专用的本机合成 Emby 服务及 `adb reverse`，不登录用户的真实服务器；设备账户、凭据和设置按原字节或原本不存在的状态恢复。合成服务的原始 UserData、临时会话、命令和本轮端口映射也独立清理并验证。

自动化与截图用于验证加载、排版和输入路径，不代替真实手柄手感、双眼可读性与佩戴舒适度验收，也不构成真实局域网或长片兼容性结论。
