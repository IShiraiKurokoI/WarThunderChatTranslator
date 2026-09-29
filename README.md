# WarThunderChatTranslator

打雷的时候看不懂对面在讲什么飞机？这里可以实时翻译聊天对话！目前支持选择Yandex，微软或google翻译接口。

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
- [ ] 实现GPT或DeepL等AI翻译（？）
- [ ] 实现自定义翻译接口（？）

## 更新记录

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
