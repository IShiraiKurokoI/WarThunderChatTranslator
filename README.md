# WarThunderChatTranslator

打雷的时候看不懂对面在讲什么飞机？这里可以实时翻译聊天对话！目前支持 Yandex、微软、Google，以及用户自定义的 AI 翻译接口。

[<img src="https://get.microsoft.com/images/zh-cn%20dark.svg" alt="从商店下载" height="96">](https://apps.microsoft.com/store/detail/9PJ8K0V3KZHF)

## 机翻生草机belike

![](doc/1.png)

![](doc/2.png)

![](doc/3.png)

![](doc/4.png)

悬浮窗可以通过点击托盘菜单的悬浮窗按钮开关，为了防止误触，悬浮窗的UI只有在焦点不在游戏内时才可交互！


## Todos

- [x] 重构至翻译界面WebUI
- [x] 支持目标语言切换
- [x] 提高代码复用性，减少重复
- [x] 实现网络代理设置
- [x] 实现游戏内覆盖
- [x] 实现 GPT / Claude 等 AI 翻译
- [x] 实现自定义 AI 翻译接口与模型管理
- [x] 支持全局快捷键语音识别、按快捷键选择目标语言并自动复制译文
- [ ] 实现界面自定义


## 更新记录

### v1.0.9.0

- 新增“互动和操作”设置页及快捷语音翻译能力。
- 新增动态全局快捷键映射与按键捕获配置。
- 语音层采用 `NAudio.Wasapi` 录音与 `sherpa-onnx` SenseVoice INT8 本地识别，不依赖 Windows 在线 `SpeechRecognizer`。
- 新增录音设备选择、输入电平、本地模型状态与四类可配置声音反馈。
- SenseVoice 使用 CPU 单线程离线推理，支持普通话、粤语、英语、日语和韩语自动识别。
- 测试项目增加真人英文与普通话语音样本及本地 ASR 结果校验。
- 包版本、程序集版本和文件版本统一为 `1.0.9.0`。

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

如果从旧源码目录覆盖升级后遇到 XAML 生成代码残留，请在 Visual Studio 中手动执行 Clean/Rebuild；项目文件不会自动删除 `obj` 中间文件。
