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
- [ ] 实现界面自定义
- [ ] 支持Bing Token

## 更新记录

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
