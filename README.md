# WarThunderChatTranslator

打雷的时候看不懂对面在讲什么飞机？这里可以实时翻译聊天对话！目前支持 Yandex、微软、Google，以及用户自定义的 AI 翻译接口。

[<img src="https://get.microsoft.com/images/zh-cn%20dark.svg" alt="从商店下载" height="96">](https://apps.microsoft.com/store/detail/9PJ8K0V3KZHF)

## 机翻生草机belike

![](doc/1.png)

![](doc/2.png)

![](doc/3.png)

## Todos

- [x] 重构至翻译界面WebUI
- [x] 支持目标语言切换
- [x] 提高代码复用性，减少重复
- [x] 实现网络代理设置
- [ ] 实现界面自定义
- [ ] 支持Bing Token
- [ ] 实现游戏内覆盖
- [x] 实现 GPT / Claude 等 AI 翻译
- [x] 实现自定义 AI 翻译接口与模型管理

## 更新记录

### v1.0.7.0

- 新增“AI 翻译（自定义）”翻译方式。
- 新增 AI 翻译模型管理页，支持添加、编辑、测试、删除和切换模型。
- 支持 OpenAI Responses、OpenAI-compatible Chat Completions 与 Anthropic Messages 三种协议，可配置 Base URL、API Key、模型和自定义提示词。
- AI 翻译统一使用结构化 JSON 返回格式，包含翻译结果、源语言和目标语言信息。
- Temperature 改为每个模型独立的可选参数，默认不发送。
- 补充 AI 翻译相关中英文 I18n 文案。
- 增加 OpenAI Responses、OpenAI-compatible Chat Completions 与 Anthropic Messages 的 mock 单元测试。

### v1.0.6.0

- Dashboard 新增 War Thunder 游戏运行状态显示：状态点与运行/未运行文字常驻显示，悬停或键盘聚焦后通过浮层显示进程 PID 与启动时间。
- Dashboard 新增日间/深夜主题切换，并使用与现有选项一致的开关样式。
- 当用户从未手动配置主题时，Dashboard 通过 `prefers-color-scheme` 自动跟随 Windows 的浅色/深色应用模式；页面打开期间系统主题发生变化时也会同步更新。
- 用户手动切换过日间/深夜模式后，将选择保存到 `localStorage`，后续优先使用用户配置，不再被系统主题自动覆盖。
- Dashboard 新增“显示聊天气泡背景”选项，可隐藏聊天消息的填充背景与边框，同时保留阵营文字颜色；设置通过 `localStorage` 持久化。
- 修正游戏进程退出后 Dashboard 运行状态可能未及时清除的问题。

### v1.0.5.0

- 将游戏聊天数据获取改为后台固定间隔轮询，默认轮询间隔为 4 秒。
- 轮询前检测 `aces.exe` 或 `aces-min-cpu.exe`，记录当前游戏进程 PID。
- 检测到新的游戏进程实例时记录日志，并清空翻译缓存、聊天缓存，同时将游戏侧 `lastId` 重置为 `0`。
- PID 仅用于标识一次 War Thunder 进程生命周期；同一个 PID 下可以经历多场对局，不将 PID 变化视为单场对局切换。
- 游戏聊天请求改为携带当前进程生命周期内已获取到的最大 `lastId`，通过增量请求减少重复数据传输和处理。
- 本地 HTTP 服务迁移到 .NET 10 的 Kestrel Minimal API，并改为异步请求处理。
- 游戏侧 HTTP 客户端复用 `HttpClient` / `SocketsHttpHandler`，减少重复创建连接带来的额外开销。
- 网页端聊天获取改为增量读取，并使用串行 `setTimeout` 调度，避免轮询请求重叠。
- 游戏轮询和网页轮询均新增独立设置项，默认均为 4 秒，可在设置页调整。
- 轮询间隔设置支持持久化保存，并同步补充对应 I18n 文案。
- 保持旧 `/gamechat` 调用方式兼容，并为进程生命周期标识保留兼容字段。
