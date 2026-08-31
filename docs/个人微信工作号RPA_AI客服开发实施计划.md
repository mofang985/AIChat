# 个人微信工作号RPA AI客服开发实施计划

| 项目 | 内容 |
| --- | --- |
| 文档名称 | 个人微信工作号RPA AI客服开发实施计划 |
| 文档版本 | v0.5 |
| 编写日期 | 2026-07-29 |
| 最后更新 | 2026-08-07 |
| 对应需求文档 | 个人微信工作号 AI 辅助客服 MVP 需求文档 |
| 对应技术设计 | 个人微信工作号RPA_AI 客服技术设计文档 |
| 实施阶段 | M4 / M4.5 核心代码闭环已完成；当前进入 M4.5 真实微信连续监听稳定性验收，重点调试 OCR/VLM 消息识别、表情占位、缓存策略和 InputOnly 安全干跑；M4.2 YOLO / ONNX 保持旁路验证 |

## 0. 当前工作进度总览

更新时间：2026-08-07。

### 0.1 已完成工作

- M1 项目骨架：后端、前端、RPA 客户端、测试项目和基础解决方案已搭建。
- M2 设备与任务基础模块：员工、客户端授权、工作号、物理主机、VM、RPA 实例、任务状态、心跳和动作日志已打通。
- M3 知识库与 AI 基础模块：商品、FAQ、售后规则、风险规则、Prompt 模板、AI Provider、关键词检索、AI 结构化回复建议和日志已接入。
- M4 单会话 RPA 闭环：当前微信会话截图、布局定位、OCR、AI 回复、风控、输入框点击、回复输入、输入校验、审核倒计时、任务结果和日志回传已打通。
- M4.2 YOLO / ONNX 旁路验证：本地 ONNX 推理骨架、调试截图和独立训练工具方案已完成，当前仍不接管真实点击坐标。
- M4.5 单会话连续监听：已支持当前打开会话的连续轮询、视觉消息流解析、待回复客户消息组、合并窗口、去重、连续失败停止和回复次数限制。
- M4.5.2 分辨率自适应布局：已接入候选布局生成、多信号评分、聊天区 / 输入区安全校验和布局调试截图。
- M4.5.3 OCR + VLM 视觉复核：已接入本地或局域网 Ollama VLM，当前测试使用 `qwen2.5vl:7b` 复核微信气泡文字和发送方。
- M4.5.4 多屏窗口锁定：已锁定微信窗口句柄、客户区、显示器边界和 DPI，输入前与发送前都会校验目标窗口。
- M4.5.5 / M4.5.7 性能诊断与缓存：已增加阶段耗时日志、布局缓存、底部最近气泡优先 OCR、气泡 OCR/VLM 缓存和预识别结果复用。
- M4.5.8 全消息 VLM 复核与气泡宽裁剪：已支持 `AllRecognizedMessages` 复核范围、客户气泡宽裁剪、微信内联表情占位输出和调试截图保存。
- M4.5.9 / M4.5.10 发送安全收口：已用 `SendMode` 替代旧布尔发送开关，默认 `InputOnly + ClearInput`，只输入和校验，不点击发送，并在校验后清空草稿。

### 0.2 当前正在进行的工作

当前处于 M4.5 真实微信连续监听稳定性验收阶段，暂不进入 M5。正在重点验证：

- OCR / VLM 对真实微信客户消息的识别准确性，包括长句、多段问题、微信内联表情、特殊标点和高风险词。
- `vision-ocr-captures` 与 `debug-captures` 调试截图是否覆盖完整客户气泡，避免只截到半句或旧气泡。
- 连续监听视觉缓存是否误复用旧消息；当前调试配置使用 `ContinuousUnchangedFrameBottomRatio=0.75` 和 `ContinuousDebugCaptureMode=Always`，优先保证可观察性。
- `InputOnly` 安全干跑链路是否稳定完成：AI 回复输入、输入框 OCR 校验、失败重试、草稿清空和清空后复核。
- 高风险或 AI 判定不应自动发送时，是否能正确停止发送链路并进入人工复核。

### 0.3 接下来的工作

- 继续用真实微信测试集覆盖典型消息：短问候、连续多问、商品咨询、售后咨询、表情混排、长句、敏感/高风险语句。
- 根据最新 `VisionOcrBubble` 截图继续修正气泡裁剪、消息合并、重复文本过滤和 VLM prompt。
- 在 M4.5 稳定后，将 `ContinuousDebugCaptureMode` 从 `Always` 调回 `OnError`，降低磁盘写入和隐私暴露面。
- 在测试号中显式切换 `SendMode=RealSendTest` 验证真实点击发送；生产前再评估 `ProductionGuarded` 所需防护条件。
- M4.5 稳定后再进入 M5 多会话队列：识别未读会话、单线程顺序切换、逐会话复用 M4/M4.5 回复闭环。

### 0.4 最终目标

最终目标是在有人值守、官方微信 Windows 客户端、全视觉 RPA 的边界内，实现店内个人微信工作号的 AI 客服自动化：员工监督设备运行，系统自动识别客户消息、调用知识库和 AI 生成低风险回复、按配置控制输入和发送频率、记录可审计日志，并逐步扩展到多会话队列、好友申请通过、欢迎语和朋友圈更新。

## 1. 实施目标

MVP 第一阶段目标不是一次性完成全部功能，而是先搭建可持续开发的项目骨架，并打通最小业务闭环：

```text
管理后台配置
↓
后端 API
↓
RPA 客户端注册与启动任务
↓
视觉识别 / OCR 获取客户消息
↓
知识库检索
↓
AI 生成回复
↓
风控判断
↓
RPA 点击输入框、键盘输入、发送前停顿、发送
↓
日志回传
```

## 2. 已确认技术决策

| 项目 | 决策 |
| --- | --- |
| 后端 | ASP.NET Core Web API |
| 前端 | React + TypeScript |
| RPA 客户端 | .NET Windows 桌面程序，MVP 使用 WPF |
| 运行方式 | 员工打开客户端后点击“开始任务” |
| 微信识别 | 严格全视觉：截图、OCR、图像识别、鼠标点击、键盘输入 |
| 虚拟化部署 | 一台高配物理主机 + 多个独立 Windows VM |
| VM 绑定 | 每个 VM 绑定一个员工、一个个人微信工作号、一个 RPA 客户端 |
| AI 供应商 | DeepSeek、通义千问，预留 OpenAI Compatible |
| 知识检索 | 关键词 + 向量混合检索 |
| 部署方式 | 优先本地局域网，兼容后续云服务器 |
| 截图存储 | 默认不保存完整截图，仅保存 OCR 文本、AI 回复、风险结果和异常脱敏截图 |

## 3. 推荐默认值

以下默认值用于项目初始化，后续可在配置中心调整：

| 参数 | 默认值 |
| --- | --- |
| 数据库 | PostgreSQL，本机开发优先连接 Docker 中现有 postgres 容器 |
| RPA UI 框架 | WPF |
| OCR 引擎 | M4 默认 Windows 系统 OCR 优先，本地 PaddleOCR 兜底 |
| M4 坐标策略 | 候选评分视觉自动定位优先，配置坐标兜底 |
| M4.2 视觉模型 | YOLO 训练，`Microsoft.ML.OnnxRuntime 1.28.0` 本地推理，先旁路验证不直接控制发送 |
| 向量存储 | MVP 先预留接口，初期可用数据库存储向量引用 |
| 发送前审核停顿 | 3 秒 |
| 最小发送间隔 | 8 秒 |
| 单轮最多处理会话数 | M4.5 仍只处理当前会话，M5 再启用多会话数量控制 |
| 每个 VM 分辨率 | 1920x1080 |
| Windows 缩放 | 100% |
| 每个 VM 建议配置 | 2 vCPU / 4-6 GB 内存 / 80 GB 磁盘 |

## 4. 项目目录结构

```text
AIChat
├─ docs
│  └─ 后续技术说明、接口文档、部署文档
├─ src
│  ├─ AIChat.Api
│  ├─ AIChat.Application
│  ├─ AIChat.Domain
│  ├─ AIChat.Infrastructure
│  ├─ AIChat.RpaClient
│  └─ AIChat.Web
├─ tests
│  ├─ AIChat.UnitTests
│  └─ AIChat.IntegrationTests
├─ AIChat.slnx
├─ Directory.Build.props
└─ global.json
```

## 5. 后端实施计划

### 5.1 项目骨架

- 创建 `AIChat.Api`。
- 创建 `AIChat.Application`。
- 创建 `AIChat.Domain`。
- 创建 `AIChat.Infrastructure`。
- 建立项目引用关系。
- 配置 Swagger / OpenAPI。
- 配置基础健康检查接口。

引用关系：

```text
AIChat.Api
├─ AIChat.Application
└─ AIChat.Infrastructure

AIChat.Application
└─ AIChat.Domain

AIChat.Infrastructure
├─ AIChat.Application
└─ AIChat.Domain
```

### 5.2 领域模型优先级

第一批：

- Employee
- EmployeeClientAccessPolicy
- WeChatWorkAccount
- DeviceHost
- VirtualDevice
- RpaClientInstance
- AutomationFeatureConfig
- RpaTask
- RpaActionLog

第二批：

- Product
- FaqItem
- AfterSaleRule
- RiskRule
- KnowledgeDocument
- KnowledgeChunk
- ReplySuggestion
- CustomerQuestion

第三批：

- LlmProviderConfig
- PromptTemplate
- AiRequestLog
- KnowledgeSearchLog
- EmbeddingRecord

### 5.3 API 优先级

第一批接口：

- 员工管理。
- 员工客户端授权管理。
- 微信工作号管理。
- 物理主机管理。
- VM 管理。
- RPA 客户端注册。
- RPA 心跳。
- 自动化配置查询。
- RPA 任务创建。
- RPA 动作日志上报。

第二批接口：

- 商品资料管理。
- FAQ 管理。
- 售后规则管理。
- 风险规则管理。
- 知识检索。
- AI 回复生成。

第三批接口：

- 任务控制台。
- 会话队列。
- AI 调用日志。
- 统计报表。

## 6. 前端实施计划

### 6.1 页面清单

MVP 第一批页面：

- 登录页。
- 首页仪表盘。
- 员工管理。
- 员工客户端授权。
- 微信工作号管理。
- 物理主机管理。
- Windows VM 管理。
- 自动化配置。
- RPA 任务控制台。

MVP 第二批页面：

- 商品资料库。
- FAQ 知识库。
- 售后规则库。
- 风险规则。
- Prompt 模板管理。
- AI 模型配置。

MVP 第三批页面：

- 会话队列。
- AI 回复记录。
- RPA 动作日志。
- AI 调用日志。
- 知识缺口列表。
- 统计报表。

### 6.2 前端实现原则

- 不做营销落地页，默认进入管理后台。
- 以配置表单、数据表格、任务状态和日志详情为主。
- 自动化任务控制按钮必须明确区分：开启、暂停、关闭、启动任务、终止任务。
- 高风险状态使用明确提示，不允许隐藏。
- 日志页面支持按员工、工作号、VM、任务、风险等级筛选。

## 7. RPA 客户端实施计划

### 7.1 客户端模块

- 客户端登录与设备绑定。
- 客户端授权状态校验。
- 物理主机 / VM / RPA 实例注册。
- 自动化配置拉取。
- 任务启动与停止。
- 微信窗口识别。
- 截图采集。
- OCR 识别接口。
- 图像定位接口。
- 鼠标点击执行。
- 键盘输入执行。
- 动作序列执行。
- 发送前审核停顿。
- 最小发送间隔。
- 紧急停止快捷键。
- 心跳和日志上报。

### 7.2 第一轮 RPA 闭环

第一轮已打通单会话代码闭环：

```text
员工启动任务
↓
识别微信窗口
↓
截图聊天区
↓
OCR 识别客户消息文本
↓
调用后端生成回复
↓
点击输入框
↓
键盘输入回复
↓
发送前停顿
↓
点击发送按钮
↓
上报日志
```

M4 固定边界：

- 仅处理员工当前打开的一个微信会话。
- 使用官方微信 Windows 客户端窗口，优先通过候选评分视觉自动定位聊天区、输入框、输入校验区和发送按钮。
- 自动定位失败或置信度不足时，`AutoWithManualFallback` 可回退 `appsettings.json` 中的配置坐标；`AutoOnly` 下直接停止，不发送。
- 使用 Windows 系统 OCR / 本地 PaddleOCR，不读取微信数据库、不使用协议、Hook、插件或非官方客户端。
- AI 返回 `ShouldAutoSend=true` 且 `RiskLevel=Low` 后才输入。
- 输入后通过输入框 OCR 校验，校验失败不发送。
- 发送前保留审核倒计时，员工可暂停或紧急停止。
- 当前默认开发联调配置 `SendMode=InputOnly`，用于验证 AI 回复、输入链路和输入框 OCR 校验但不点击发送；真实发送验收必须显式切换为 `SendMode=RealSendTest` 或 `ProductionGuarded`。

### 7.3 M4.2 YOLO / ONNX 视觉识别旁路验证

M4.2 是视觉定位增强阶段，目标是验证 YOLO / ONNX 模型是否能比当前 OpenCV 规则更稳定地识别微信界面区域。

M4.2 只做旁路验证，不直接替换当前 M4 执行链路：

- M4 仍使用 OpenCV 布局定位执行 OCR、输入和发送。
- YOLO / ONNX 并行识别微信客户区截图。
- YOLO 结果只用于调试图、日志和识别质量评估。
- 模型加载失败、推理失败或置信度不足时，不影响当前 M4 主流程。
- 未经验证稳定前，不使用 YOLO 坐标执行真实点击或发送。

第一版识别标签：

| 标签 | 说明 |
| --- | --- |
| `conversation_list` | 左侧会话列表 |
| `chat_content` | 右侧聊天内容区 |
| `input_area` | 底部输入区整体 |
| `input_box` | 底部输入框 |
| `send_button` | 发送按钮 |
| `customer_message_bubble` | 客户消息气泡 |
| `self_message_bubble` | 自己消息气泡 |

实施步骤：

```text
采集测试微信截图
↓
标注 YOLO 数据集
↓
训练 YOLO 模型
↓
导出 ONNX
↓
RPA 客户端加载 ONNX
↓
与 OpenCV 结果并行输出调试截图
↓
统计识别命中率和置信度
↓
决定是否进入 M4.3
```

M4.2 验收标准：

- 主要区域 `conversation_list`、`chat_content`、`input_area`、`input_box` 命中率达到 90% 以上。
- 有待发送内容时，`send_button` 命中率达到 90% 以上。
- `customer_message_bubble` 与 `self_message_bubble` 能基本区分，命中率达到 80% 以上。
- 覆盖至少 1920x1080、2560x1440 和一种非标准窗口尺寸。
- YOLO 调试截图能直观看到模型结果与 OpenCV 结果差异。

### 7.4 M4.3 YOLO 优先 / OpenCV 兜底切换

M4.3 只有在 M4.2 识别稳定后才进入。

目标策略：

```text
YOLO / ONNX 坐标优先
↓
OpenCV 布局规则兜底
↓
appsettings.json 配置坐标最后兜底
↓
低置信度一律不发送
```

M4.3 才允许使用 YOLO 结果参与真实 OCR 裁剪、输入框点击、发送按钮点击和消息气泡定位。

### 7.5 M4.5.1 单会话连续自动回复

M4.5.1 是 M4 和 M5 之间的过渡阶段，目标是在不切换会话、不扫描未读列表的前提下，让当前打开的一个微信会话支持连续自动回复，并能把客户连续发送的多条未回复消息合并为一组问题生成综合回复。当前代码已接入，下一步进入真实微信会话验收。

执行流程：

```text
员工打开目标客户会话
↓
点击开始连续监听
↓
截图完整聊天区并解析视觉消息列表
↓
最新有效消息是客户则提取待回复客户消息组；最新是我方则进入轮询等待
↓
定时截图聊天区
↓
识别客户 / 我方 / 系统 / 未知消息列表
↓
从下往上判断最新有效消息，生成待回复客户消息组并去重
↓
客户连续多条消息合并为一个 CustomerQuestion
↓
调用后端生成 AI 回复
↓
风控与知识库命中校验
↓
输入回复、审核倒计时、发送后校验
↓
等待最小发送间隔
↓
继续监听当前会话
```

M4.5.1 固定边界：

- 只监听员工当前打开的一个微信会话。
- 不自动切换其他客户会话。
- 不扫描左侧未读会话列表。
- 不处理好友申请、欢迎语和朋友圈。
- 仍然坚持全视觉方案，不读取微信数据库、不使用协议、Hook、插件或非官方客户端。
- 每一轮回复仍必须满足 `ShouldAutoSend=true`、`RiskLevel=Low`、输入框校验通过和发送后清空校验。
- 员工可随时暂停、停止或接管。

M4.5.1 关键能力：

- 当前会话定时监听间隔配置，例如 `ContinuousPollIntervalSeconds`。
- 单次连续监听最长时长配置，例如 `MaxContinuousSessionMinutes`。
- 同一消息去重，避免重复回复。
- 连续多条客户消息组稳定窗口，例如 `MessageMergeWindowSeconds`。
- 默认 `ReplyGroupingMode=Combined`，一组客户问题只生成一条综合回复。
- 回复后最小发送间隔沿用 M4 的 `MinSendIntervalSeconds`。
- 监听中如 OCR 低置信度、AI 转人工、高风险或发送失败，则暂停连续自动回复并提示员工处理。
- RPA 客户端 UI 增加连续监听状态、已处理消息摘要、最近一次轮询时间和本轮回复次数。

M4.5.1 不新增多会话队列。它只解决“同一个客户继续追问时，是否能继续自动回复，以及连续多问是否能合并覆盖回复”的问题。

### 7.5.2 M4.5.2 分辨率自适应视觉布局引擎

M4.5.2 是 M4.5.1 真实微信验收中暴露出的视觉稳定性增强阶段，目标是在不同分辨率、微信最大化和 Windows 缩放变化时，稳定截取聊天内容区和底部输入区，避免把聊天消息误当输入框内容，或漏掉底部最新客户消息。

关键实现：

- `WeChatLayoutDetector.DetectAsync` 对外入口保持不变，不新增后端 API、数据库表或 Migration。
- 内部新增 `WeChatLayoutCandidate`、`WeChatLayoutCandidateScore` 和 `WeChatLayoutCandidateScorer`。
- 底部输入区上边界不再只取单条最高差值横线，而是生成多个候选布局。
- 候选来源包括长横线、发送按钮反推、输入区白底变化和保守比例兜底。
- 评分信号包括横线覆盖率、输入区白底比例、输入区高度合理性、发送按钮位置、聊天区有效高度和消息气泡数量。
- 几何安全校验要求聊天区和输入区不得重叠，`ConversationContextRegion` 不得包含输入框，`InputVerifyRegion` 必须完全在输入区内。
- `layout-captures` 会绘制候选输入区上边界、候选来源和候选分数，便于人工核对。
- 发送后如果输入校验区仍识别到明显非空文本，客户端判定发送后校验异常，不直接标记发送成功。

验收重点：

- 在 `1920x1080`、`2560x1440` 和至少一种高分辨率/缩放组合下测试微信最大化窗口。
- `layout-captures` 中聊天区应覆盖到底部最新客户消息上方，输入区应完整覆盖底部输入区域。
- 输入校验区不得覆盖聊天气泡；输入后 OCR 应识别 AI 回复，不应识别旧聊天消息。
- 客户连续发送多条消息时，视觉消息流应能合并为待回复客户消息组。
- `CoordinateMode=AutoOnly` 下，候选分数不足或几何校验失败必须停止，不允许继续点击发送。

### 7.6 第二轮 RPA 闭环

#### 7.6.1 M5.1 / M5.2 / M5.3 / M5.4 多会话未读扫描与受控切换

M5.1 先实现“只读队列”：RPA 客户端复用自动布局输出的 `ConversationListRegion` 截取微信左侧可见会话列表，通过本地 OpenCV 红色候选 + 白色数字字形检测生成“带数字未读角标”的候选队列。M5.2 在此基础上对每个候选会话行做只读 OCR，提取会话名、最新消息摘要、时间和未读数字。M5.3 增加连续扫描稳定性预演，只有同一候选在多次扫描中指纹一致且行位置未明显漂移时，才标记为“可切换候选”。M5.4 增加受控会话切换入口，只允许点击首个“可切换候选”，点击前重新扫描复核，点击后 OCR 校验右侧聊天标题；该阶段仍不输入、不发送、不调用 AI 回复、不创建后端回复任务。

关键实现：

- 新增 `UnreadConversationQueueScanner`，只依赖 `ScreenCaptureService` 截取 `ConversationListRegion`，先识别红色角标候选，再用 `UnreadNumberGlyphDetector` 过滤掉不带数字的纯红点。
- 新增 `WeChatConversationListRegionPlanner`，会话列表左边界按微信左侧导航栏宽度估算，右边界使用聊天区分割线，避免只截到会话列表右侧一小段。
- 新增 `UnreadConversationQueueSnapshot`、`UnreadConversationCandidate`、`UnreadBadgeDetection` 和 `UnreadConversationQueueAnalyzer`，负责候选去重、视觉顺序排序、行范围推断和最大候选数限制。
- 新增 `UnreadConversationRowOcrModels`、`UnreadConversationRowOcrPlanner` 和 `UnreadConversationRowOcrParser`，按候选行裁剪整行文本区域，使用快速 Windows UI OCR 解析会话名、摘要、时间和数字角标信息。
- `UnreadConversationQueueScanner` 注入 `PaddleOcrEngine`，只对已通过数字角标过滤的候选行做一次整行 OCR；OCR 结果仅用于 UI 展示，不进入回复决策。
- 新增 `UnreadConversationQueueStabilityTracker`、`UnreadConversationReadOnlyPreflight` 和候选指纹模型，按“会话名或未命名占位 + 摘要 + 时间 + 未读数字或数字角标占位 + 行位置”连续比对候选稳定性。
- 新增 `UnreadConversationSwitchModels` 和 `UnreadConversationControlledSwitcher`，负责筛选首个可切换候选、计算行内点击点、点击前窗口锁复核、点击后聊天标题 OCR 校验；标题 OCR 为空但候选行呈现微信选中态时，使用选中态截图作为回退校验。
- 新增配置：`EnableUnreadQueueReadOnlyScan`、`UnreadQueueScanIntervalSeconds`、`MaxUnreadQueueCandidates`、`UnreadQueueMinConfidence`、`EnableUnreadQueueDebugCaptures`、`UnreadQueueDebugCaptureDirectory`、`EnableUnreadQueueReadOnlyPreflight`、`UnreadQueueRequiredStableScanCount`、`UnreadQueueStableRowTolerancePixels`、`UnreadQueueStabilityCacheMinutes`、`EnableUnreadQueueControlledSwitch`、`UnreadQueueSwitchPostClickVerifyDelayMs`。
- `MainWindow` 增加“扫描未读队列（只读）”按钮、“切换首个可切换候选（受控）”按钮和“未读队列（只读）”展示区；连续监听期间也按 `UnreadQueueScanIntervalSeconds` 节流刷新只读候选，并优先展示“会话名｜未读数｜摘要｜时间｜预演状态”。
- 手动坐标兜底不会生成 `ConversationListRegion`，因此会话列表区域为空时只展示跳过原因，不猜测坐标。

后续 M5.5+ 范围：

- 点击后进一步校验右侧最新消息与队列摘要的一致性。
- 构建单线程真实处理队列。
- 复用 M4/M4.5 当前会话回复闭环处理队首会话。
- 高风险会话跳过自动发送。
- 异常停止和任务恢复。

### 7.7 第三轮 RPA 闭环

- 好友申请识别。
- 好友申请自动通过。
- 欢迎语自动输入和发送。
- 客户来源选择。
- 单次处理上限。

## 8. AI 与知识库实施计划

### 8.1 AI 服务

第一批：

- `ILlmProvider` 抽象。
- `DeepSeekProvider`。
- `TongyiProvider`。
- Prompt 模板加载。
- 结构化输出解析。
- AI 调用日志。

第二批：

- 场景化模型配置。
- 超时和重试。
- Token 统计。
- 失败降级。
- 风险复核模型。

### 8.2 知识库

第一批：

- 商品资料管理。
- FAQ 管理。
- 售后规则管理。
- 关键词检索。
- 引用来源输出。

第二批：

- 知识文档。
- 知识片段。
- Embedding 生成。
- 向量检索。
- 混合检索。
- 知识命中分数。

第三批：

- 知识缺口记录。
- 知识有效期。
- 索引重建。
- Excel 导入。

## 9. 多 VM 部署实施计划

### 9.1 本地环境准备

- 准备一台高配物理主机。
- 安装虚拟化平台。
- 创建多个 Windows VM。
- 每个 VM 固定分辨率和缩放比例。
- 每个 VM 安装官方微信 Windows 客户端。
- 每个 VM 安装 RPA 客户端。
- 每个 VM 绑定一个员工和一个微信工作号。

### 9.2 管理后台支持

- 物理主机登记。
- VM 登记。
- RPA 客户端实例登记。
- 员工和 VM 绑定。
- 微信工作号和 VM 绑定。
- VM 在线状态展示。
- VM 异常状态展示。

### 9.3 运行约束

- 一个 VM 只允许一个 RPA 客户端在线。
- 一个 VM 只允许一个微信号运行。
- 一个 RPA 客户端同一时间只允许一个任务。
- 物理主机资源不足时，不启动新任务。
- VM 离线时，不分配任务。

## 10. 测试计划

### 10.1 后端测试

- 自动化配置状态校验。
- 员工客户端授权有效期校验。
- 员工离职或禁用后的 RPA 强制停止。
- 工作时间段校验。
- RPA 客户端注册和心跳。
- 任务状态流转。
- AI 调用失败降级。
- 知识库未命中处理。
- 高风险规则命中。

### 10.2 前端测试

- 自动化配置表单。
- 任务控制台状态刷新。
- 日志筛选。
- 高风险提示。
- VM 在线离线状态展示。

### 10.3 RPA 测试

- 微信窗口识别。
- 聊天区 OCR。
- YOLO / ONNX 旁路识别区域命中率。
- YOLO 调试截图和 OpenCV 调试截图对比。
- 当前单会话连续监听。
- 新消息去重和连续消息合并。
- 鼠标点击会话、输入框和发送按钮。
- 键盘输入。
- 输入框内容校验。
- 发送前审核停顿。
- 紧急停止。
- 单线程多会话队列。
- VM 分辨率变化后的自动定位、配置兜底和校准提示。

### 10.4 多 VM 测试

- 单物理主机运行多个 VM。
- 每个 VM 独立注册 RPA 客户端。
- 两个 VM 同时执行任务互不影响。
- 某个 VM 掉线不影响其他 VM。
- 某个 VM OCR 异常只停止该 VM 任务。

## 11. MVP 里程碑

| 里程碑 | 目标 | 状态 |
| --- | --- | --- |
| M1 项目骨架 | 后端、前端、RPA 客户端项目创建完成 | 已完成 |
| M2 设备与任务 | 员工、客户端授权、工作号、物理主机、VM、RPA 实例、任务状态打通 | 已完成 |
| M3 知识库与 AI | 商品、FAQ、售后规则、AI 回复生成打通 | 已完成 |
| M4 单会话 RPA | 单个微信会话 OCR、自动坐标定位、输入、发送、日志闭环 | 已完成代码闭环，当前继续真实微信测试调通 |
| M4.2 YOLO / ONNX 视觉识别验证 | ONNX 旁路推理骨架已接入；模型训练工具和样本沉淀流程已具备，继续旁路评估 | 进行中 |
| M4.3 YOLO 优先 / OpenCV 兜底 | 在模型稳定后切换视觉定位策略 | M4.2 验证通过后进入 |
| M4.5.1 单会话连续自动回复 | 当前会话定时监听、消息组去重、连续追问综合回复、发送后校验 | 已完成代码接入，进入真实微信验收 |
| M4.5.2 分辨率自适应视觉布局 | 候选输入区上边界、多信号评分、几何安全校验、候选调试截图 | 已完成代码接入，进入多分辨率真实微信验收 |
| M4.5.3 OCR + VLM 视觉复核 | OCR 可疑时调用本地或局域网 Ollama VLM 复核单条消息气泡截图 | 已完成代码接入，进入真实微信验收 |
| M4.5.4 多屏窗口锁定 | 启动时锁定微信窗口句柄，显示监听屏幕、窗口坐标和目标窗口状态 | 已完成代码接入，进入多屏真实微信验收 |
| M4.5.5 性能诊断与加速 | 阶段耗时日志、底部最近气泡 OCR、布局缓存、VLM 复核范围配置、连续监听预识别结果复用 | 已完成代码接入，进入真实微信耗时验收 |
| M4.5.7 连续监听识别加速 | 聊天区底部画面指纹跳过、气泡 OCR/VLM 缓存、VLM 复用首次 OCR、正常轮询少写调试截图 | 已完成代码接入，进入真实微信耗时验收 |
| M4.5.8 全消息 VLM 复核与气泡宽裁剪 | 全可见消息 VLM 复核、客户气泡宽裁剪、表情占位、重复文本过滤 | 已完成代码接入，正在真实微信识别准确性验收 |
| M4.5.9 真实发送安全模式 | `SendMode=DryRun/InputOnly/RealSendTest/ProductionGuarded`，UI 展示发送模式，真实发送显式开启 | 已完成代码接入，默认 InputOnly 联调 |
| M4.5.10 InputOnly 安全干跑闭环 | 输入 AI 回复、OCR 校验、失败重试、清空草稿、清空后 OCR 复核 | 已完成代码接入，正在真实微信 InputOnly 验收 |
| M5 多会话队列 | 多客户未读会话单线程顺序处理 | M4.5 稳定后进入 |
| M6 好友申请 | 好友申请通过和欢迎语发送 | 待开始 |
| M7 管理后台 | 配置、任务、日志、统计页面可用 | 待开始 |
| M8 试运行 | 单主机多 VM 环境试运行 | 待开始 |

## 12. 当前轮开发状态

当前已完成到 M4，系统已经具备：

- PostgreSQL 持久化和 EF Core migration。
- 设备、员工、授权、VM、RPA 客户端实例和任务基础模块。
- 商品资料、FAQ、售后规则、风险规则、Prompt 模板、AI Provider 配置。
- 关键词检索、AI 结构化回复建议、风控判断、AI 请求日志和知识检索日志。
- Web 管理后台 M2/M3 基础页面骨架。
- RPA 客户端注册、心跳和授权状态展示。
- RPA 客户端候选评分视觉自动定位优先和配置坐标兜底。
- 微信窗口标题定位。
- 微信客户区布局自动定位、候选输入区上边界评分、运行坐标生成、Windows 系统 OCR 和本地 PaddleOCR 兜底。
- OCR 裁剪区域本机调试截图保存，用于 M4 坐标校准。
- 布局标注截图本机保存，用于确认候选输入区上边界、消息区、输入框和发送按钮自动定位结果。
- 后端 RPA 任务结果回写接口。
- AI 回复建议调用和风控结果展示。
- 鼠标点击输入框、`ClipboardPaste` / `KeyboardTyping` 两种输入模式、输入框 OCR 校验。
- 发送前审核倒计时、暂停、紧急停止和动作日志回传。
- M4.2 YOLO / ONNX 旁路推理骨架，模型缺失或未开启时不影响 M4 主流程。
- M4.5.1 单会话连续监听按钮、视觉消息流解析、待回复客户消息组、新消息轮询、消息组指纹去重、合并窗口、连续失败停止和回复次数限制。
- M4.5.2 分辨率自适应布局检测：候选生成、多信号评分、聊天区/输入区不重叠校验、输入校验区越界拦截和发送后非空文本异常拦截。
- M4.5.3 OCR + VLM 视觉复核：OCR 可疑时复核单条消息气泡截图，VLM 失败时默认跳过本轮并继续监听。
- M4.5.5 性能诊断与加速：输出窗口定位、布局检测、气泡检测、每条 OCR、每次 VLM、AI 调用和截图保存耗时；连续监听优先 OCR 底部最近气泡，缓存可用布局，并把预识别结果传入单次回复闭环避免重复整屏识别。
- M4.5.7 连续监听识别加速：画面未变化时复用上一轮视觉结果，气泡 hash 相同时复用 OCR/VLM 合并结果，VLM 复核复用第一次 OCR，不再重复 OCR 同一气泡。
- M4.5.4 多屏窗口锁定：启动单次任务或连续监听时锁定微信窗口句柄、标题、客户区坐标、显示器边界和 DPI；连续监听每轮按锁定句柄复用目标窗口，输入前和发送前再次校验锁定目标。
- M4.5.9 真实发送安全模式：用 `SendMode=DryRun/InputOnly/RealSendTest/ProductionGuarded` 替代旧布尔开关，默认 `InputOnly`，UI 顶部醒目展示当前发送模式，动作日志和任务结果写入 `SendMode`。
- M4.5.10 InputOnly 安全干跑闭环：默认 `InputOnlyAfterVerifyAction=ClearInput`，输入校验通过后清空微信输入框并再次 OCR 复核；如果输入校验为空或未命中，先重新激活锁定微信窗口、清空输入框并按 `InputVerifyRetryCount` 重试；如果剪贴板被占用导致粘贴失败，则退回逐字符键盘输入，避免默认联调模式留下可误发草稿。

当前下一步继续验收 M4.5.2 / M4.5.3 / M4.5.4 / M4.5.5 / M4.5.7 真实微信连续对话：

- 用测试微信会话验证不同分辨率下的自动定位结果。
- 查看客户端“坐标模式”是否显示 `视觉自动定位2.0` 或 `配置坐标兜底`。
- 查看 `%LOCALAPPDATA%\AIChat\RpaClient\layout-captures` 中的布局标注截图。
- 确认候选输入区上边界红线落在真实聊天区和底部输入区之间。
- 查看 `%LOCALAPPDATA%\AIChat\RpaClient\vision-ocr-captures` 中的 VLM 复核气泡截图和复核结果。
- 默认 `SendMode=InputOnly` 且 `InputOnlyAfterVerifyAction=ClearInput` 时，确认 OCR 客户消息、AI 低风险回复、微信输入、输入框校验、必要时输入重试、草稿清空和清空后 OCR 复核可闭环，但不会点击发送。
- 需要真实发送验收时，先显式切换为 `SendMode=RealSendTest` 或 `ProductionGuarded`，再确认审核倒计时、点击发送和发送后校验均可闭环。
- 查看运行日志中的 `[性能]` 项，确认窗口定位、布局检测、气泡检测、每条 OCR、每次 VLM、AI 回复接口和调试截图保存耗时都能输出。
- 连续监听第二轮开始，如果微信窗口尺寸未变，应看到“布局缓存命中”，不再每轮重复布局检测。
- 连续监听 OCR 日志应显示“仅 OCR 底部最近 N 个”，默认最多 8 个，避免每轮识别整屏历史气泡。
- 当前测试配置下，VLM 复核日志可出现在本轮识别到的非系统气泡；同一气泡再次出现时应优先命中缓存，不应每轮重复调用 VLM。
- 连续监听触发回复时，应看到“已复用连续监听预识别结果”，避免先轮询识别一遍、进入单次回复又完整识别一遍。
- 在多屏环境下查看客户端“锁定目标”和运行日志里的 `Handle` / `Monitor` / `Client` / `DPI`，验证微信窗口位于第二屏或左侧扩展屏时截图和点击仍跟随锁定窗口。
- 验证连续监听过程中移动、关闭或切换目标微信窗口时，窗口锁定校验会停止本轮，不继续输入或发送。
- 若要验证 M4.2，开启 `EnableYoloLayoutValidation=true`，放入 `wechat-layout.onnx` 和 `labels.txt` 后查看 `%LOCALAPPDATA%\AIChat\RpaClient\yolo-captures`。

M4.2 YOLO / ONNX 视觉识别验证后续范围：

- 收集测试微信截图样本，覆盖不同分辨率、缩放、输入状态和消息形态。
- 标注会话列表、聊天内容区、底部输入区、输入框、发送按钮、客户消息气泡和自己消息气泡。
- 使用 YOLO 训练模型并导出 ONNX。
- RPA 客户端本地加载 ONNX 模型进行旁路推理。
- 输出 YOLO 与 OpenCV 结果的对比标注截图。
- 统计各标签命中率、置信度和缺失情况。
- 不直接使用 YOLO 坐标执行真实发送，避免影响当前 M4 验收。

M4.3 YOLO 优先 / OpenCV 兜底后续范围：

- 当 YOLO 识别稳定后，切换为 YOLO 坐标优先。
- OpenCV 布局规则作为兜底。
- 配置坐标作为最后兜底。
- 低置信度、标签缺失或关键区域冲突时一律不发送。

M4.5.1 单会话连续自动回复当前已完成代码接入：

- 定时监听当前打开的单个客户会话。
- 解析视觉消息列表，识别最新有效消息发送方，并提取待回复客户消息组。
- 合并客户短时间内连续发送的多条消息为一个 `CustomerQuestion`。
- 默认用一条综合回复覆盖客户消息组，不逐条连续发送。
- 每轮复用 M4 单会话 OCR、AI 回复、风控、输入、审核倒计时和发送后校验。
- 控制监听间隔、连续运行时长、每轮回复次数和最小发送间隔。
- 出现低置信度、风险拦截、AI 转人工、发送失败或员工暂停时停止连续自动回复。

M4.5.1 真实微信验收重点：

- 默认按视觉消息流启动：最新有效消息是客户消息则提取待回复客户消息组并立即回复，最新有效消息是我方消息则等待客户下一条新消息。
- 客户连续发送“您好 / 你是谁？ / 你能帮我做什么？”时，应生成一条覆盖整组问题的综合回复。
- 每轮回复创建独立 `RpaTask`，同一次监听共用 `single-continuous-{yyyyMMddHHmmss}` 形式的 `ConversationKey`。
- YOLO / ONNX 仍只做旁路识别和样本沉淀，不控制真实点击坐标。

M5 多会话队列后续范围：

- 识别微信左侧未读会话。
- 构建待处理会话队列。
- 单线程顺序切换客户会话。
- 每个会话复用 M4 单会话闭环。
- 控制每轮处理数量、最小发送间隔和异常跳过。
- 增加暂停恢复后的队列状态处理。

## 13. M4.5.3 OCR + VLM 视觉复核

M4.5.3 已接入 RPA 客户端本地视觉复核层。Windows OCR / PaddleOCR 仍是第一层识别；当前真实测试将 `ContinuousVisionReviewScope` 调整为 `AllRecognizedMessages`，本轮识别到的非系统气泡都会执行一次 VLM 复核，避免 OCR 高置信错字直接进入 AI；后续如需进一步提速，可把 `ContinuousVisionReviewScope` 调回 `PendingCustomerGroupOnly`，或把 `VisionReviewMode` 调回 `SuspiciousOnly`。

默认配置：

```json
{
  "EnableVisionOcrReview": true,
  "VisionOcrProvider": "Ollama",
  "VisionOcrBaseUrl": "http://localhost:11434",
  "VisionOcrModel": "qwen2.5vl:7b",
  "VisionReviewMode": "AlwaysForCustomerMessages",
  "VisionOcrFailureBehavior": "SkipAndContinue"
}
```

本阶段不新增后端 API、不新增数据库表。VLM 只复核消息文字和发送方，不生成客服回复、不绕过后端 AI 风控。VLM 失败时默认跳过当前可疑消息并继续监听，不再因为 OCR 低置信度直接停止连续监听或转人工。

详细说明见 `M4.5.3_OCR_VLM视觉复核说明.md`。

## 13.1 M4.5.4 多屏窗口锁定与监听目标可视化

M4.5.4 是一个小型稳定性增强项，目标是回答并解决“当前监听的是哪个屏幕、哪个微信窗口”的问题。RPA 启动时仍按窗口标题查找首次目标窗口，但启动后会锁定该窗口的句柄、客户区和显示器信息；如果桌面上存在多个微信窗口，连续监听不再每轮重新选择面积更大的窗口。

已实现范围：

- 启动“开始任务”或“开始连续监听”时，锁定本次选中的微信窗口句柄、标题、客户区坐标、所在显示器边界和 DPI。
- 连续监听轮询时优先通过锁定句柄复用窗口，不再只按标题重新选择窗口。
- 每轮识别前校验锁定窗口仍存在且可见，句柄、标题、显示器、DPI 和客户区坐标仍匹配。
- RPA 客户端 UI 显示当前监听目标：窗口标题、窗口句柄、客户区坐标、显示器边界和 DPI。
- 运行日志输出当前截图和点击使用的窗口坐标，方便员工确认监听目标。
- 锁定窗口失效、被关闭、标题变化、显示器变化、DPI 变化或尺寸突变时，停止本轮并提示员工重新打开目标会话。
- 单次闭环在输入前和发送前再次校验锁定窗口，避免审核倒计时期间目标窗口变化后继续点击。

本增强不新增后端 API、不新增数据库表，不改变 M4.5 当前只监听单个微信会话的边界。

## 13.2 M4.5.5 性能诊断与加速

M4.5.5 是当前连续监听调试中的性能小升级，目标是把“准备执行”和“OCR 客户消息识别”阶段拆成可观察的真实耗时，并减少每轮重复计算。

默认配置：

```json
{
  "EnablePerformanceDiagnostics": true,
  "ContinuousMaxVisualMessagesToOcr": 8,
  "ContinuousVisualOcrBottomRatio": 0.60,
  "ContinuousVisionReviewScope": "AllRecognizedMessages",
  "EnableContinuousLayoutCache": true
}
```

实现范围：

- UI 显示真实阶段：正在定位窗口、正在布局检测、布局缓存命中、正在识别底部最近气泡、正在 OCR 气泡、正在 VLM 复核气泡、正在生成 AI 回复。
- 运行日志统一输出 `[性能]` 前缀，覆盖窗口定位、布局检测、聊天区截图、截图解码、气泡候选检测、每条 OCR、每次 VLM、AI 回复接口和调试截图保存耗时。
- 连续监听只优先 OCR 聊天区底部最近候选气泡，默认最多 8 个，避免每轮 OCR 整屏历史消息。
- OCR 队列会过滤居中的时间 / 系统分割线，以及左右两侧头像或图标类非文字候选，减少无效 OCR 和 Unknown 噪声。
- 高置信 OCR 文本如果在中文句子中混入 `/`、`\`、`|`、`_`、`—`、`–` 等异常符号，会触发 VLM 复核，降低“查询一下”识别成“查询—下”这类错别字进入 AI 的概率。
- 微信窗口句柄和客户区尺寸不变时复用上一次可用布局结果，跳过布局检测。
- VLM 当前真实测试复核范围为 `AllRecognizedMessages`，本轮已识别的非系统气泡都会走一次 VLM；同一气泡命中缓存后不重复调用。后续性能稳定后可把 `ContinuousVisionReviewScope` 调回 `PendingCustomerGroupOnly`，或把 `VisionReviewMode` 调回 `SuspiciousOnly`。
- 连续监听确认需要回复后，将已识别的窗口、布局和视觉消息流传给单次回复闭环，避免重复执行窗口定位、布局检测和整屏气泡 OCR。
- 消息合并窗口不再每秒重复 OCR，改为等待 `MessageMergeWindowSeconds` 结束后复查一次；如果客户消息组变化，再重新等待一个合并窗口。

本增强不改变真实发送准入条件，不新增后端 API、数据库表或 Migration。M4.5.9 已进一步把真实发送入口收口到 `SendMode=RealSendTest/ProductionGuarded`，M4.5.10 已让默认 `InputOnly` 在输入校验后清空草稿；后续如果 `[性能]` 日志显示主要耗时仍集中在合并窗口轮询，可再增加截图指纹缓存或降低合并窗口内轮询频率。

## 13.3 M4.5.7 连续监听识别加速

M4.5.7 是在 M4.5.5 之后的识别缓存加速项，目标是在不降低 OCR + VLM 准确性的前提下，减少连续监听无新消息时的重复计算。

默认配置：

```json
{
  "EnableContinuousVisualCache": true,
  "ContinuousVisualCacheMaxEntries": 80,
  "EnableContinuousUnchangedFrameSkip": true,
  "ContinuousUnchangedFrameBottomRatio": 0.75,
  "ContinuousDebugCaptureMode": "Always",
  "ContinuousReviewLatestSelfMessage": true
}
```

实现范围：

- 连续监听每轮计算聊天区底部画面指纹；画面未变化时直接复用上一轮视觉消息流结果，跳过气泡检测、OCR 和 VLM。
- 气泡截图按 hash、发送方候选和尺寸缓存 OCR/VLM 合并结果；同一屏历史气泡不再每轮重复 OCR。
- VLM 二次复核复用第一次 OCR 的 `OcrResult` 和气泡截图，不再为了复核重新 OCR 同一个气泡。
- 最新有效消息为我方时，默认也会对这一条我方消息做一次 VLM 复核，提升日志展示和后续聊天记录清洗准确性，但不触发 AI 回复。
- 客户左侧文字候选会扩展到更宽的客户消息区域后再交给 OCR/VLM，避免长句只截到紧贴文字的一小段导致 VLM 读不完整。
- 微信内联表情会由 VLM 尝试输出为 `[机智]` 等文本占位符；无法确认具体含义时输出 `[表情]`，避免表情被直接省略或误并入相邻文字。
- 缓存只在一次“开始连续监听”会话内有效；窗口句柄、客户区或聊天区变化时清空。
- 当前真实调试阶段保存每个气泡截图和 VLM 复核截图；识别稳定后可把 `ContinuousDebugCaptureMode` 改回 `OnError`，减少磁盘写入。
- 日志输出画面跳过、气泡缓存命中、OCR/VLM 实际执行次数和缓存跳过次数。

验收重点：

- 无新消息连续监听多轮时，应看到“聊天区底部画面未变化，跳过气泡检测、OCR 和 VLM”。
- 同一屏历史气泡不应每轮重复出现 OCR/VLM 耗时日志。
- 客户发送新消息后，仍应识别最新待回复客户消息组，并按 `AllRecognizedMessages` 对本轮非系统气泡执行 VLM 复核或缓存命中。

## 14. 暂不实现内容

当前仍暂不实现：

- 使用 YOLO 坐标直接执行真实点击和发送。
- 多会话队列。
- 好友申请自动通过和欢迎语。
- 朋友圈自动更新。
- 随机鼠标轨迹和随机抖动。
- 真实向量检索。
- AI 知识缺口自动归档。
- Excel / 文档导入知识库。

这些能力从 M5 起按里程碑逐步实现。
