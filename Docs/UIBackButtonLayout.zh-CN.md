# 页面返回按钮布局

## 调整范围

页面标题栏统一将返回箭头放在标题左侧，使用 64 单位按钮与 16 单位间隔。标题使用剩余宽度；同一标题栏的操作按钮贴齐内容区右侧，填补原返回按钮留下的位置。

- 隐私政策、权限说明、第三方许可：返回在左侧，语言切换或源码导出在最右侧。
- 音轨与内嵌字幕：返回在标题前。
- SMB 添加／编辑、连接中、共享选择及连接表单：返回或取消在标题前；编辑页的删除配置在最右侧。
- Emby 账户及登录表单：已有的返回媒体库、返回或取消登录入口改为标题前的箭头；账户页仍按原条件显示返回媒体库入口。

文件浏览、Emby 媒体库和快捷字幕浏览已经使用左侧返回入口。演员列表的向左翻页、播放器停止、键盘完成等操作继续保留各自含义。各入口沿用原有回调及取消逻辑。

## 验证记录

2026-10-17 使用 Unity **6000.3.23f1** 验证：

- 编辑器脚本编译通过，没有编译错误。
- 以下现有 EditMode 测试共 **230 / 230 通过**，无失败或跳过：`UIRedesignTests`、`PrivacyConsentTests`、`QuickControlsTests`、`EmbyServerAddressTests`、`EmbyLibraryViewsTests`。可在 Test Runner 的 `VRPlayer.Tests` 程序集中选择这五个测试类复现。
- 在独立工程快照中导出 **51 张原生 uGUI 预览**，核对受影响标题栏的箭头位置、标题间距及操作按钮右对齐。
- `git diff --check` 通过。

预览沿用 `VRPlayer.Editor.UIRedesignPreview.ExportBatch`，在快照内补充法律页面、SMB 表单和 Emby 登录状态；共享枚举使用仅存在于快照中的合成响应，保存截图前完成待生成卡片。补充脚本保存在忽略的 `Tools/.runtime/ui-header-layout/prepare-preview.py`，原始截图、源码校验和与日志保存在忽略的 `Docs/Captures/UIHeaderLayout-20261017/`。

截图只包含合成元数据，`192.0.2.10` 是手工填写的预览示例，不是实际服务器。以下选图已检查无私人媒体、账户或凭据。

![受影响页面的标题栏布局](Images/UIBackButtons/HeaderLayouts.png)

本轮未执行 APK 构建、真实 SMB／Emby 联网验收或 PICO 佩戴测试；编辑器预览和自动测试不代表实体控制器操作或头显舒适度验收。
