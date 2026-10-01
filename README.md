# WarThunderChatTranslator

打雷的时候看不懂对面在讲什么飞机？这里可以实时翻译聊天对话！目前支持 Yandex、微软、Google，以及用户自定义的 AI 翻译接口。

[<img src="https://get.microsoft.com/images/zh-cn%20dark.svg" alt="从商店下载" height="96">](https://apps.microsoft.com/store/detail/9PJ8K0V3KZHF)

## 机翻生草机belike

![](doc/1.png)

![](doc/2.png)

![](doc/3.png)

![](doc/4.png)

悬浮窗可以通过点击托盘菜单的悬浮窗按钮开关，为了防止误触，悬浮窗的UI只有在焦点不在游戏内时才可交互！

## 快捷语音翻译

从 `互动和操作` 页面启用快捷语音翻译后，可通过系统级快捷键在 War Thunder 位于前台时直接开始一次语音识别。默认快捷键为：

- `Ctrl+Alt+1` → English
- `Ctrl+Alt+2` → Russian
- `Ctrl+Alt+3` → German
- `Ctrl+Alt+4` → Japanese
- `Ctrl+Alt+5` → Korean

第一次按快捷键开始录音，再按一次同一快捷键会立即请求结束录音，并将 Windows 尚未提交的识别结果一并刷新后进入翻译；如果不再次按下，连续识别也会在检测到静音后自动结束。识别完成后，文本会通过当前选择的翻译接口翻译为该快捷键对应的目标语言，并自动写入 Windows 剪贴板。默认五组映射会显示在可展开列表中；快捷键通过“录入 → 实际按组合键 → 完成”的方式绑定，不需要手工输入按键名称，并支持继续新增、删除或修改任意数量的快捷键与目标语言映射。

语音识别使用 Windows `Windows.Media.SpeechRecognition`，因此该功能需要以具有包身份的 MSIX 版本运行，并需要麦克风权限和 Windows“联机语音识别”。设置页会自动检查这两项：开启快捷语音翻译时会主动请求可请求的麦克风权限；仍未满足条件时会在页面顶部显示警告并提供打开 Windows 隐私设置的入口。启用功能后，应用会在后台创建并编译一个长生命周期 `SpeechRecognizer`，后续录音复用同一实例，从而把大部分初始化延迟移出游戏内的快捷键触发路径。切换识别语言时会自动重新预热；如果设备变化、休眠恢复或 Windows 语音服务重启导致旧实例失效，开始录音时会自动重建并重试一次。设置页会显示“正在预热 / 已就绪 / 正在录音 / 不可用”等状态。

录音开始、录音结束、翻译成功和翻译失败四类提示音均支持系统提示音、Windows TTS、自定义音频。录音开始时机可选择“提示音开始播放时立即录音”或“提示音播放结束后开始录音”，默认采用前者，更适合戴耳机游戏时快速开口；后者可用于外放环境避免提示音被麦克风收录。TTS 可分别设置提示文本、系统已安装的 Microsoft 音色和语速，并按提示文本、音色与语速自动缓存；自定义音频支持 WAV、MP3、M4A、WMA，并会按事件分别保留原文件名复制到应用本地数据目录；设置页同时显示所选文件名和原始路径。

## Todos

- [x] 重构至翻译界面WebUI
- [x] 支持目标语言切换
- [x] 提高代码复用性，减少重复
- [x] 实现网络代理设置
- [x] 实现游戏内覆盖
- [x] 实现 GPT / Claude 等 AI 翻译
- [x] 实现自定义 AI 翻译接口与模型管理
- [ ] 实现界面自定义
- [ ] 支持Bing Token
- [x] 支持全局快捷键语音识别、按快捷键选择目标语言并自动复制译文

## 更新记录

### v1.0.9.0

- 新增“互动和操作”设置页中的快捷语音翻译功能。
- 支持系统级全局快捷键，例如 `Ctrl+Alt+1`，在游戏处于前台时也可触发一次语音识别。
- 默认提供 5 组快捷键映射，并支持在可展开列表中动态新增、删除和修改映射；快捷键通过录入按钮捕获真实按键组合，不需要手工输入；识别结果通过当前选择的翻译接口翻译后自动写入 Windows 剪贴板。
- 新增录音开始、录音结束、翻译成功、翻译失败四组独立声音反馈；每组均支持系统提示音、Windows TTS 提示语、自定义音频三种模式。
- 语音识别改为在线预热方案：启用功能或应用启动时后台创建并编译长生命周期 `SpeechRecognizer`，录音时直接复用；切换识别语言会重新预热，旧实例失效时会自动重建并重试一次。
- 恢复“开始录音时机”设置：默认在录音开始提示音开始播放时立即启动识别，也可选择等待提示音播放结束后再开始录音。
- 设置页新增语音识别状态显示，可查看正在预热、已就绪、正在录音和不可用状态。
- TTS 支持为四组事件分别设置文本、系统已安装的 Microsoft 音色与语速；生成结果按“提示文本 + 音色 + 语速”自动缓存，避免重复合成，并支持手动清理缓存。
- 自定义提示音按事件分别复制到应用本地数据目录，避免原文件移动后失效；支持 WAV、MP3、M4A、WMA，并显示真实文件名和原始路径。
- 新增麦克风权限和 Windows 联机语音识别状态检测；开启功能时自动请求麦克风权限，条件未满足时在设置页顶部显示警告并可跳转 Windows 隐私设置。
- 优化快捷键动态列表与自定义音频卡片的响应式布局，避免窄窗口内容溢出。
- 语音识别失败保留独立系统提示音；翻译/剪贴板失败升级为可配置的系统音、TTS 或自定义音频。
- 新增快捷键解析及 TTS 缓存键单元测试。
- 包版本、程序集版本和文件版本统一更新至 `1.0.9.0`。

### v1.0.8.0

- 新增悬浮窗功能

### v1.0.7.0

- 新增“AI 翻译（自定义）”翻译方式。
- 新增 AI 翻译模型管理页，支持添加、编辑、测试、删除和切换模型。
- 支持 OpenAI Responses、OpenAI-compatible Chat Completions 与 Anthropic Messages 三种协议，可配置 Base URL、API Key、模型和自定义提示词。
- 增加 OpenAI Responses、OpenAI-compatible Chat Completions 与 Anthropic Messages 的 mock 单元测试。

### v1.0.6.0

- Dashboard 新增 War Thunder 游戏运行状态显示：状态点与运行/未运行文字常驻显示，悬停或键盘聚焦后通过浮层显示进程 PID 与启动时间。
- Dashboard 新增日间/深夜主题切换和“显示聊天气泡背景”选项。
- 修正游戏进程退出后 Dashboard 运行状态可能未及时清除的问题。

### v1.0.5.0

- 重构服务实现方式，大幅优化程序性能。
- 网页端聊天获取改为增量读取，并使用串行 `setTimeout` 调度，避免轮询请求重叠。
- 游戏轮询和网页轮询均新增独立设置项，默认均为 4 秒，可在设置页调整。
- 轮询间隔设置支持持久化保存，并同步补充对应 I18n 文案。

### WinUI XAML generated-code cache after upgrading
If a source upgrade reports `CS1061` from `obj/.../Pages/InteractPage.g.cs` for controls/events that no longer exist in `Pages/InteractPage.xaml`, close Visual Studio and run `CleanBuildArtifacts.cmd`, then reopen the solution and use **Build > Rebuild Solution**. These errors are caused by stale generated XAML files left in `obj` when newer source files are copied over an existing checkout.

### v1.0.9.0 quick speech stop reliability

- Manual stop now enters an explicit **Stopping** state immediately.
- A completed `StopAsync()` explicitly releases the recognition pipeline instead of relying only on the `Completed` event.
- Manual stop has a timeout/cancel fallback so the UI cannot remain stuck in Recording indefinitely.
- The latest speech hypothesis is retained as a fallback for very short utterances that are stopped before a final result is emitted.
