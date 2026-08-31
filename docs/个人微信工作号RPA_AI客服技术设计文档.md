# 个人微信工作号RPA AI客服技术设计文档

| 项目 | 内容 |
| --- | --- |
| 文档名称 | 个人微信工作号RPA AI客服技术设计文档 |
| 文档版本 | v0.5 |
| 编写日期 | 2026-07-29 |
| 最后更新 | 2026-08-07 |
| 对应需求文档 | 个人微信工作号RPA AI客服 MVP 需求文档 |
| 技术阶段 | M4 / M4.5 当前单会话连续监听代码闭环已完成；当前进行真实微信 OCR/VLM 识别准确性、InputOnly 安全干跑和连续监听稳定性验收；M4.2 YOLO / ONNX 保持旁路验证 |
| 核心目标 | 在员工值守下，通过全视觉 RPA + AI + 知识库，实现个人微信工作号的好友申请处理、欢迎语发送和低风险客户消息自动回复 |

## 1. 技术决策摘要

| 项目 | 决策 |
| --- | --- |
| 后端 | ASP.NET Core Web API + EF Core + PostgreSQL |
| 前端 | React 管理后台 |
| 本地 RPA 客户端 | .NET Windows 桌面程序，MVP 使用 WPF |
| RPA 形态 | 员工打开客户端后点击“开始任务”执行单次回复，或点击“开始连续监听”监听当前会话新消息 |
| 微信识别方式 | 严格全视觉：截图、OCR、图像识别、鼠标点击、键盘输入 |
| 微信号部署 | MVP 默认一台高配物理主机承载多个独立 Windows 虚拟机，每个员工一个独立 VM、一个个人微信工作号 |
| AI 供应商 | DeepSeek、通义千问，架构预留 OpenAI Compatible |
| 知识检索 | 关键词 + 向量混合检索 |
| 部署方式 | 优先支持本地局域网服务器，同时兼容后续迁移云服务器 |
| 截图存储 | 默认不保存完整截图，仅保存 OCR 文本、AI 回复、风险结果和异常时脱敏截图 |
| M4 RPA 实现 | Windows 系统 OCR 优先 + 本地 PaddleOCR 兜底 + 微信布局候选评分自动定位优先 + 配置坐标兜底 + Windows API 鼠标键盘输入 |
| M4.2 视觉增强 | YOLO 训练 + `Microsoft.ML.OnnxRuntime 1.28.0` 本地推理，先旁路验证，不直接接管真实点击和发送 |
| M4.5.1 连续监听 | 当前会话视觉消息流解析、待回复客户消息组、定时截图、新消息去重、合并窗口、复用 M4 单次回复闭环 |
| M4.5.2 布局稳定性 | OpenCV 候选布局生成 + 多信号评分 + 几何安全校验，适配不同分辨率和微信最大化窗口 |
| M4.5.3 OCR 复核 | OCR 后调用本地或局域网 Ollama VLM 复核微信气泡截图，当前真实调试使用 `qwen2.5vl:7b` 与 `AllRecognizedMessages` |
| M4.5.4 窗口锁定 | 已接入启动时锁定微信窗口句柄、显示器、DPI 和客户区坐标，降低多屏或多个微信窗口误选风险 |
| M4.5.5 / M4.5.7 性能优化 | 连续监听耗时诊断、布局缓存、底部最近气泡优先 OCR、气泡 OCR/VLM 缓存、复用预识别结果 |
| M4.5.8 识别准确性 | 全消息 VLM 复核、客户气泡宽裁剪、微信内联表情占位、重复文本过滤 |
| M4.5.9 / M4.5.10 发送安全 | `SendMode` 收口真实发送入口，默认 `InputOnly + ClearInput`，只输入校验并清空草稿，不点击发送 |

## 2. 虚拟机部署策略与一机多号边界

为节约硬件成本，MVP 默认采用“一台高配物理主机 + 多个独立 Windows 虚拟机”的部署方式。

推荐模型：

```text
1 台高配物理主机
├─ Windows VM A
│  ├─ 员工 A
│  ├─ 官方微信 Windows 客户端 A
│  └─ RPA 客户端实例 A
├─ Windows VM B
│  ├─ 员工 B
│  ├─ 官方微信 Windows 客户端 B
│  └─ RPA 客户端实例 B
└─ Windows VM N
   ├─ 员工 N
   ├─ 官方微信 Windows 客户端 N
   └─ RPA 客户端实例 N
```

该方案的关键原则：

- 一个 Windows VM 只登录一个个人微信工作号。
- 一个 Windows VM 只运行一个 RPA 客户端实例。
- 一个 Windows VM 只绑定一个员工。
- RPA 只操作当前 VM 内前台可见的微信窗口。
- 不在同一个 Windows 系统内多开多个微信客户端。
- 不使用微信多开器、模拟器、插件、Hook、DLL 注入或非官方客户端。
- 每个 VM 的截图、OCR、任务、动作日志、配置和异常状态必须相互隔离。

不建议“一台 Windows 系统直接管理多个微信号”。

原因：

- 账号风险更高：多开、模拟器、非官方工具和批量化行为更容易触发平台风险。
- 技术稳定性更差：多个微信窗口会增加会话定位、输入框定位和发送目标识别难度。
- 误发风险更高：RPA 如果切错窗口或会话，可能把客户 A 的回复发给客户 B。
- 值守责任不清：一个员工同时监督多个号时，异常接管难度明显上升。
- 审计复杂度更高：任务、会话、动作日志需要额外区分窗口实例和账号上下文。

技术设计默认采用：

```text
1 台物理主机
↓
多个独立 Windows VM
↓
每个 VM 绑定 1 名员工
↓
每个 VM 登录 1 个个人微信工作号
↓
每个 VM 运行 1 个 RPA 客户端实例
```

如果后续必须在同一个 Windows 系统内直接运行多个微信客户端，需要单独立项评估，不纳入 MVP。

## 3. 总体架构

```text
React 管理后台
↓
ASP.NET Core Web API
├─ 员工与工作号管理
├─ 自动化配置服务
├─ 知识库服务
├─ 混合检索服务
├─ AI 大模型服务
├─ 风控服务
├─ RPA 任务编排服务
├─ 日志与审计服务
└─ 统计服务
↓
数据与基础设施
├─ PostgreSQL
├─ 向量检索存储
├─ 文件存储：异常脱敏截图
└─ 局域网 / 云端部署环境
↓
高配物理主机
├─ Windows VM A
│  ├─ .NET Windows RPA 客户端 A
│  └─ 微信 Windows 客户端 A
├─ Windows VM B
│  ├─ .NET Windows RPA 客户端 B
│  └─ 微信 Windows 客户端 B
└─ Windows VM N
   ├─ .NET Windows RPA 客户端 N
   └─ 微信 Windows 客户端 N
↓
每个 RPA 客户端
├─ 微信窗口识别
├─ 截图采集
├─ OCR 识别
├─ 图像定位
├─ 鼠标点击执行
├─ 键盘输入执行
├─ 单线程会话队列
├─ 审核停顿与频率控制
└─ 紧急停止
```

## 4. 系统边界

### 4.1 后端服务边界

后端负责业务配置、AI、知识库、风控、任务编排、日志和统计。

后端不直接操作微信客户端，不保存微信登录凭证，不读取微信本地数据库。

### 4.2 Windows RPA 客户端边界

RPA 客户端运行在员工专属 Windows VM 内，负责该 VM 内前台可见窗口自动化：

- 识别微信窗口。
- 截取微信界面画面。
- OCR 识别文本。
- 图像定位按钮、输入框、会话列表。
- 执行鼠标点击。
- 执行键盘输入。
- 执行发送动作。
- 上报执行日志。

RPA 客户端不调用“客服回复生成”AI 模型，不保存客服回复模型 API Key，不直接访问数据库。M4.5.3 允许 RPA 客户端调用本地或局域网 VLM 做单条消息气泡 OCR 复核；该 VLM 只用于识别截图中文字和发送方，不生成客服回复、不绕过后端风控。

每个 RPA 客户端必须携带：

- 物理主机标识。
- VM 标识。
- 设备实例标识。
- 员工标识。
- 微信工作号标识。

### 4.3 React 管理后台边界

管理后台负责：

- 员工和工作号管理。
- 商品、FAQ、售后规则维护。
- AI 模型和 Prompt 配置。
- 自动化功能配置。
- RPA 任务监控。
- 会话队列查看。
- 风险和执行日志查看。
- 统计报表。

## 5. 部署架构

### 5.1 本地局域网部署

MVP 优先支持本地局域网部署。

```text
店内局域网服务器
├─ Web API
├─ React 静态站点
├─ PostgreSQL
├─ 向量检索服务
└─ 文件存储

高配物理主机
├─ Windows VM A：微信客户端 A + RPA 客户端 A
├─ Windows VM B：微信客户端 B + RPA 客户端 B
└─ Windows VM N：微信客户端 N + RPA 客户端 N
```

特点：

- 各 Windows VM 通过局域网访问后端。
- 截图和 OCR 结果不离开店内网络。
- 对外只需要 AI 模型 API 出口。
- 适合早期内测、门店本地管理和多员工低成本部署。

### 5.2 物理主机与 VM 规划

MVP 默认使用高配物理主机承载多个 Windows VM。

资源规划建议：

| 资源 | 建议 |
| --- | --- |
| CPU | 按每个 VM 至少 2 vCPU 预估 |
| 内存 | 按每个 VM 至少 4-6 GB 预估 |
| 磁盘 | 每个 VM 独立系统盘，预留日志和临时截图空间 |
| 显示 | 每个 VM 需要保持可见桌面会话，供全视觉 RPA 截图和操作 |
| 网络 | VM 需访问局域网后端和外部 AI API |

VM 隔离规则：

- 每个 VM 使用独立 Windows 用户环境。
- 每个 VM 只安装官方微信 Windows 客户端和一个 RPA 客户端。
- 每个 VM 独立登录一个个人微信工作号。
- 每个 VM 独立绑定一个员工。
- 每个 VM 的 RPA 任务、OCR 文本、异常截图和动作日志独立记录。
- VM 暂停、关机、断网时，后端应将对应 RPA 客户端标记为离线。

### 5.3 云服务器部署

后续可迁移到云服务器。

```text
云服务器
├─ Web API
├─ React 静态站点
├─ PostgreSQL / 云数据库
├─ 向量检索服务
└─ 文件存储 / 对象存储

高配物理主机 / 员工终端
├─ Windows VM A：微信客户端 A + RPA 客户端 A
├─ Windows VM B：微信客户端 B + RPA 客户端 B
└─ Windows VM N：微信客户端 N + RPA 客户端 N
```

兼容要求：

- RPA 客户端通过 HTTPS 访问后端。
- 支持客户端设备授权。
- 支持配置本地局域网地址或云端 API 地址。
- 异常截图默认脱敏后再上传。

## 6. 技术栈设计

### 6.1 后端

建议：

- ASP.NET Core Web API。
- Entity Framework Core。
- PostgreSQL。
- SignalR 或轮询接口用于任务状态同步。
- BackgroundService 处理索引同步、日志清理、统计汇总。

主要分层：

```text
Api
Application
Domain
Infrastructure
```

### 6.2 前端

建议：

- React。
- TypeScript。
- 组件库按项目后续实际选择。
- 管理后台以表格、配置表单、任务控制台、日志详情为主。

### 6.3 Windows RPA 客户端

建议：

- .NET Windows 桌面程序。
- UI 采用 WPF，稳定、实现成本低。
- OCR、图像定位、鼠标键盘执行通过独立接口封装。

客户端模块：

```text
RpaClient
├─ AppShell
├─ Auth
├─ TaskRunner
├─ ScreenCapture
├─ Ocr
├─ VisionLocator
├─ MouseKeyboardExecutor
├─ SingleConversationReplyCycleExecutor
├─ ContinuousConversationTaskRunner
├─ QueueProcessor
├─ SafetyGuard
└─ LogUploader
```

### 6.4 OCR 与视觉识别

严格全视觉要求下，MVP 不使用微信协议、不读取本地数据库、不使用 Hook。

OCR 设计为接口：

```text
IOcrEngine
├─ PaddleOcrEngine
└─ WindowsOcrEngine
```

图像定位设计为接口：

```text
IVisionLocator
├─ TemplateImageLocator
├─ RegionTextLocator
├─ LayoutRuleLocator
└─ YoloOnnxVisionLocator
```

MVP 推荐：

- M4 默认优先使用 Windows 系统 OCR，失败或为空时回退本地 PaddleOCR 中文模型。
- M4 优先通过微信客户区截图自动定位聊天消息区、输入框、输入校验区和发送按钮。
- M4.5.2 已将 `WeChatLayoutDetector` 升级为候选布局生成 + 多信号评分 + 几何安全校验。
- M4.5.3 新增 `VisionOcrReviewer`：当前真实测试使用 `AllRecognizedMessages`，本轮识别到的非系统气泡都会调用 Ollama VLM 复核，避免 OCR 高置信错字直接进入 AI；默认模型 `qwen2.5vl:7b`。
- VLM 失败时默认跳过当前可疑消息并继续监听，不直接触发连续监听停止或转人工；客服回复仍必须由后端 AI 生成并通过 `ShouldAutoSend=true`、`RiskLevel=Low`、输入校验和发送后校验。
- M4.5.4 已增强 `WeChatWindowLocator`：任务启动时锁定目标微信窗口句柄、标题、客户区坐标和所在显示器信息，连续监听轮询时优先复用该锁定窗口。
- M4.5.5 / M4.5.7 新增性能诊断和加速：输出 `[性能]` 分段耗时日志，连续监听只 OCR 底部最近候选气泡，气泡 OCR/VLM 结果按 hash 缓存，窗口尺寸不变时复用布局结果。
- M4.5.8 当前用于真实微信识别准确性调试：客户气泡宽裁剪、全消息 VLM 复核、微信内联表情占位和重复文本过滤。
- M4.5.9 / M4.5.10 将真实发送收口到 `SendMode`，默认 `InputOnly + ClearInput`，只输入、校验并清空草稿，不点击发送。
- 自动定位失败或置信度不足时，`AutoWithManualFallback` 可回退 `appsettings.json` 中的配置坐标；`AutoOnly` 直接停止。
- 调试阶段可保存本机布局标注截图，用于确认候选输入区上边界、最终聊天区、输入区、输入校验区和发送按钮是否覆盖真实区域。

M4.5.2 布局检测规则：

- 输入区上边界候选来自底部横线扫描、发送按钮反推、输入区白底变化和保守比例兜底。
- 默认搜索微信客户区高度 55% 到 92%，输入区高度比例约束为 8% 到 38%。
- 评分信号包括横线覆盖率、输入区白底比例、输入区高度合理性、发送按钮位置、聊天区有效高度和消息气泡数量。
- `ConversationContextRegion` 必须完全落在聊天内容区内，不能包含输入框、工具栏或左侧会话列表。
- `InputVerifyRegion` 必须完全落在底部输入区内，发送后若仍识别到明显非空文本则判定发送后校验异常。
- YOLO / ONNX 在 M4.5.2 仍保持旁路验证，不接管真实点击坐标。

M4.2 视觉增强：

- 引入 YOLO / ONNX 作为全视觉目标检测验证方案。
- 第一阶段只做旁路验证：YOLO 结果只用于调试截图、日志和识别质量评估。
- 当前 M4 的真实点击、输入和发送仍使用 OpenCV 布局结果。
- 模型稳定后，M4.3 再考虑切换为 YOLO 优先、OpenCV 兜底、配置坐标最后兜底。
- YOLO 识别目标包括会话列表、聊天内容区、底部输入区、底部输入框、发送按钮、客户消息气泡和自己消息气泡。
- 当前实现类为 `YoloOnnxVisionDetector`，在 `SingleConversationTaskRunner` 中作为旁路执行；模型缺失、未开启或推理异常时只写动作日志，不阻塞 M4 主流程。

YOLO / ONNX 推理模块设计：

```text
WeChat 客户区截图
↓
图片归一化到模型输入尺寸
↓
ONNX Runtime 本地推理
↓
NMS 去重
↓
输出检测框、标签和置信度
↓
映射回真实屏幕坐标
↓
保存对比调试图
```

M4.2 暂不要求后端参与模型推理，模型文件部署在 RPA 客户端本机：

```text
%LOCALAPPDATA%\AIChat\RpaClient\models\wechat-layout\
```

默认文件名：

- `wechat-layout.onnx`
- `labels.txt`

调试输出目录：

```text
%LOCALAPPDATA%\AIChat\RpaClient\yolo-captures
```

## 7. 核心模块设计

### 7.1 员工与工作号管理

职责：

- 管理员工账号。
- 绑定个人微信工作号登记信息。
- 绑定物理主机、Windows VM 和 RPA 客户端实例。
- 管理员工 RPA 客户端使用授权和使用期限。
- 控制员工可操作的数据范围。

关键规则：

- 一个员工默认绑定一个工作号。
- 一个员工默认绑定一个 Windows VM。
- 一个 Windows VM 默认只登录一个工作号。
- 一个 Windows VM 默认只运行一个 RPA 客户端实例。
- 一个 RPA 客户端实例默认同一时间只运行一个 RPA 任务。
- 一个物理主机可以承载多个 Windows VM，但不得在同一个 VM 内运行多个微信号。
- 员工离职、停用、授权过期后，不允许登录 RPA 客户端，不允许启动新任务。
- RPA 客户端启动、心跳、创建任务时必须校验员工授权状态和使用期限。

### 7.2 自动化配置服务

职责：

- 管理每项自动化能力的开启、暂停、关闭。
- 管理开始时间、结束时间。
- 管理适用工作号。
- 管理单次上限、每日上限。
- 管理发送前审核停顿和最小发送间隔。

自动化功能：

| 功能 | MVP 默认状态 |
| --- | --- |
| 好友申请自动处理 | 关闭 |
| 欢迎语自动发送 | 关闭 |
| 客户消息自动回复 | 关闭 |
| 高风险自动拦截 | 开启 |
| 多会话队列处理 | 开启 |
| 朋友圈文案生成 | 开启 |

启动校验：

```text
读取配置
↓
判断状态是否开启
↓
判断当前时间是否在生效时间段
↓
判断工作号是否在适用范围
↓
判断单次 / 每日上限
↓
创建任务
```

### 7.3 RPA 任务编排服务

职责：

- 创建 RPA 任务。
- 下发任务配置。
- 接收任务心跳。
- 接收任务状态。
- 接收动作日志。
- 记录异常停止原因。

RPA 任务状态：

```text
待启动
运行中
已暂停
已完成
已终止
异常停止
```

### 7.4 单线程会话队列

多个客户同时来消息时，RPA 客户端本地维护单线程队列。

处理规则：

- 只扫描当前微信号的会话列表。
- 只入队未读会话。
- 队列先进先出。
- 当前会话处理完成后才处理下一个。
- 同一客户连续多条消息合并为一次上下文。
- 队列处理数量达到单轮上限后停止。
- 员工暂停时停止处理下一项。

### 7.4.1 M4.5.1 当前会话连续监听

M4.5.1 不启用多会话队列，只监听员工当前打开的一个微信会话。它在 M4.5 的“最新有效消息发送方”基础上，进一步提取末尾连续客户消息组，默认用一条综合回复覆盖整组问题。

核心对象：

| 对象 | 职责 |
| --- | --- |
| `SingleConversationReplyCycleExecutor` | 复用 M4 单次 OCR、AI、风控、输入、倒计时、真实发送和发送后校验闭环 |
| `ContinuousConversationTaskRunner` | 编排视觉消息流解析、当前会话轮询、消息合并、去重和停止条件 |
| `ChatMessageVisualExtractor` | 从完整聊天内容区识别客户 / 我方 / 系统 / 未知消息列表，并按视觉位置判断最新有效消息 |
| `CustomerMessageGroup` | 合并末尾连续客户消息，生成本轮待回复问题组、上下文和消息组指纹 |
| `CustomerMessageExtractor` | 生成客户消息快照、上下文和消息指纹，并保留旧 OCR 调试兜底能力 |
| `ContinuousConversationState` | 保存已回复指纹、短期重复文本、回复次数和连续失败次数 |

流程：

```text
点击开始连续监听
↓
截图完整聊天内容区并解析视觉消息列表
↓
从下往上判断最新有效消息发送方，跳过系统提示
↓
最新是客户消息则提取末尾连续客户消息组；最新是我方消息则等待
↓
按 ContinuousPollIntervalSeconds 轮询当前会话
↓
发现新的待回复客户消息组
↓
等待 MessageMergeWindowSeconds 合并窗口
↓
复用 SingleConversationReplyCycleExecutor 执行一轮回复
↓
发送成功后记录指纹并继续监听
```

关键边界：

- 每轮真实回复仍创建一个 `RpaTask`，任务类型继续使用 `ReplyMessage`。
- 启动时直接解析当前可见消息列表：最新有效消息是客户消息就提取待回复客户消息组并立即复用单次回复闭环，最新有效消息是我方消息就等待客户下一条消息。
- 后端历史成功任务只作为审计和辅助排查来源，不再作为“是否需要回复”的主判断依据。
- 连续监听默认不再使用左侧客户 OCR 兜底来触发回复，避免微信系统提示或旧客户气泡误判。
- 默认 `ReplyGroupingMode=Combined`，一组客户连续消息只生成一条综合回复；本阶段不逐条连续发送。
- 同一次连续监听共用一个 `ConversationKey`，格式为 `single-continuous-{yyyyMMddHHmmss}`。
- 轮询无新消息时只写客户端本地日志，不创建后端空任务。
- M4.5 连续监听不切换会话、不扫描未读、不处理好友申请；M5.1 的未读队列扫描是独立只读能力，只展示候选队列，不驱动自动回复链路。
- M4.5.4 已在连续监听启动时锁定当前目标微信窗口，并在 UI 和日志中展示窗口标题、句柄、客户区坐标、显示器边界和 DPI，方便员工确认监听的是哪块屏幕。
- M4.5.5 中，连续监听触发本轮回复时会复用已识别的窗口、布局和视觉消息流，避免进入单次回复闭环后再次做完整 OCR/VLM。
- YOLO / ONNX 在 M4.5 仍只做旁路识别和调试图，不控制真实点击坐标。
- 发送准入不降低：默认 `SendMode=InputOnly` 不点击发送；真实点击发送必须显式切换为 `RealSendTest` 或 `ProductionGuarded`，并同时满足 `ShouldAutoSend=true`、`RiskLevel=Low`、输入框校验、审核倒计时和发送后校验通过。

### 7.4.2 M5.1 / M5.2 / M5.3 / M5.4 多会话未读扫描与受控切换

M5.1 是进入完整多会话队列前的安全观察阶段，M5.2 在只读候选队列上补充候选行 OCR，M5.3 增加连续扫描稳定性与切换前只读预演，M5.4 增加受控会话切换。RPA 客户端先读取官方微信当前可见的左侧会话列表，生成本地数字未读候选队列，并展示会话名、最新消息摘要、时间、未读数字和预演状态给员工确认；只有候选稳定且会话名可靠时，才允许点击首个可切换候选。M5.4 点击后只做右侧聊天标题 OCR 校验，不输入、不发送、不调用 AI 回复、不创建后端任务。

执行流程：

```text
点击“扫描未读队列（只读）”或连续监听轮询触发
↓
定位官方微信窗口并运行自动布局检测
↓
读取 WeChatLayoutResult.ConversationListRegion
↓
截取左侧可见会话列表区域
↓
OpenCV HSV 红色阈值识别未读角标候选，并按微信会话行几何与白色数字字形过滤：只接受头像右上角、尺寸接近未读角标、内部存在白色数字笔画像素的小红块
↓
按数字角标所在会话行去重、按屏幕从上到下排序、限制最大候选数，并把蓝色候选行框对齐到会话列表行高
↓
对每个候选行只读裁剪昵称、摘要、时间、数字角标区域，使用 OCR 生成可读队列信息
↓
生成候选指纹并与前序扫描结果比对，输出“观察中 / 可切换候选”只读预演状态
↓
员工点击“切换首个可切换候选（受控）”时，重新定位窗口、检测布局、扫描队列并选择首个稳定候选
↓
点击前校验窗口锁和候选行范围，点击候选行中心偏右位置
↓
点击后 OCR 右侧聊天标题，与目标会话名比对
↓
刷新 WPF 未读队列状态和切换结果日志，仍不输入、不发送、不调用 AI 回复
```

关键边界：

- `UnreadConversationQueueScanner` 只依赖 `ScreenCaptureService`、OpenCV 和 `PaddleOcrEngine`，不引用 `MouseKeyboardExecutor`、`SingleConversationReplyCycleExecutor`、`RpaBackendClient` 或 AI 回复链路。
- `UnreadConversationControlledSwitcher` 是 M5.4 唯一会点击左侧会话行的组件；它只接受 `Preflight.IsStable=true` 且会话名非空的候选。
- `UnreadConversationQueueAnalyzer` 只处理 `UnreadBadgeDetection` 矩形数据：同一会话行只保留置信度更高或面积更大的角标，输出 `UnreadConversationCandidate`；`UnreadConversationRowOcrParser` 只把候选行 OCR 结果挂到 `TextInfo` 用于 UI 展示。
- `ConversationListRegion` 只在自动布局成功时可用；手动坐标兜底区域为空时直接显示跳过原因。
- `ConversationListRegion` 由 `WeChatConversationListRegionPlanner` 生成：左边界按客户区宽度估算微信导航栏右侧，右边界使用聊天区左分割线，覆盖员工实际看到的完整会话列表列。
- 连续监听中只在布局检测后、聊天气泡 OCR 前按 `UnreadQueueScanIntervalSeconds` 节流刷新未读队列，不改变 `state.Evaluate`、消息合并和 `_cycleExecutor.ExecuteAsync` 的触发条件。
- 调试截图只保存本地标注图，路径由 `UnreadQueueDebugCaptureDirectory` 控制；该截图不代表已处理、已输入或已发送任何会话。

M5.1.1 识别修正：

- 仅凭“红色”不能判定未读，头像、公众号封面、昵称强调色、右侧红色文字、群聊免打扰小红点都会误触发；当前 OpenCV 只把位于头像右上角范围、面积填充率足够高且内部存在白色数字字形的小红块作为数字未读角标候选。
- 纯红点未读、屏蔽群聊红点、公众号无数字红点不进入 M5.1 队列；本阶段只展示红点内带数字的未读会话。
- 调试图中红色外框表示完整 `ConversationListRegion` 扫描范围；蓝色框表示数字角标候选会话行，不代表点击区域，M5.1 不会点击该行。
- 蓝色候选行框按会话列表估算行高对齐，避免直接围绕红点中心上下扩展导致跨搜索框或跨相邻会话。
- 可以使用 YOLO 做下一版训练定位，但应作为 `m5.1-yolo-unread` 新模型版本追加标签，不直接覆盖 M4.2 旧模型；建议先追加 `unread_badge`，必要时再追加 `conversation_list_item`。RPA 默认仍保持 YOLO 旁路或只读候选，不接管点击/发送。

M5.2 可读队列增强：

- `UnreadConversationRowOcrPlanner` 原按候选行拆分昵称、摘要、时间、角标 4 个 OCR 子区域；M5.3 性能修正后，扫描阶段改为每个候选只裁剪一次整行文本区域并调用快速 Windows UI OCR，避免 5 个候选触发 20 次 OCR 和 PaddleOCR 回退。
- `UnreadConversationRowOcrParser` 支持从整行 OCR 文本中解析会话名、摘要、时间和角标数字，并归一化微信时间文本，例如 `16：13` 转为 `16:13`、`I/l/丨` 转为 `1`。
- WPF 队列优先显示 `会话名｜未读 n｜摘要｜时间`；OCR 为空时仍保留数字角标候选并标记观察中，不再直接跳过可见候选。
- 候选行 OCR 不改变队列候选排序，不作为自动回复条件，不写入后端任务；后续 M5.4 若要点击队首会话，仍需新增会话一致性校验。

M5.3 只读预演增强：

- `UnreadConversationQueueStabilityTracker` 在本地内存中缓存候选指纹，指纹优先由会话名、摘要、时间、未读数字和候选行中心位置组成；会话名缺失但摘要、时间或角标数字存在时使用“未命名会话”占位，角标数字 OCR 缺失时使用“数字角标”占位，因为候选本身已通过数字字形检测。
- 默认 `UnreadQueueRequiredStableScanCount=2`：同一候选连续两次扫描一致且行中心漂移不超过 `UnreadQueueStableRowTolerancePixels` 时，标记为“可切换候选”；首次出现、还未达到次数或 OCR 文本为空时标记为“观察中”。
- M5.3 不再把可见数字角标候选标记为“跳过”；OCR 完全为空时只作为“仅观察角标位置”的观察中候选，后续切换前仍需再次 OCR 校验。
- 预演结果只进入 `UnreadConversationCandidate.Preflight` 和 UI 文本；不点击、不切换、不写后端、不作为自动回复条件。
- 调试图候选标签会附加 `stable`、`pending` 或 `skip`，用于核对预演状态，不代表执行动作。

M5.4 受控切换增强：

- 受控切换入口为 WPF 按钮“切换首个可切换候选（受控）”，默认只处理当前扫描中视觉顺序最靠前的可切换候选。
- 点击前重新执行窗口定位、布局检测、未读队列扫描和 M5.3 稳定性预演，不复用过期 UI 列表。
- 点击前后都使用 `WeChatWindowLock` 校验窗口句柄、标题、客户区、显示器和 DPI，窗口变化则阻止或标记失败。
- 点击点由 `UnreadConversationSwitchPlanner.CreateClickPoint` 计算，落在候选行内中心偏右位置，避免点头像和未读角标。
- 点击后优先截取更完整的右侧聊天标题栏区域并调用快速 UI OCR，`TitleMatchesTarget` 与目标会话名不匹配时记录失败；如果标题 OCR 为空但左侧候选行已呈现微信选中绿色背景，则作为“选中态校验通过”回退成功，同时保存标题截图和行截图用于人工复核。
- 标题校验失败日志会包含 `UnreadSwitchTitle` / `UnreadSwitchSelectedRow` 调试截图路径，便于核对 OCR 空结果是标题区域偏移、字体太小还是微信标题隐藏导致。
- M5.4 仍没有自动循环处理队列；每次点击都由员工显式触发。

当前限制：

- 只识别当前屏幕可见未读会话；列表外、被遮挡、未滚动到可视区的会话不会入队。
- 未读数量、会话名、摘要、时间、预演状态和点击后标题校验来自只读 OCR 与本地连续扫描缓存，当前只用于员工观察、受控切换前置条件和调试，不作为自动回复条件。
- 本阶段没有后端队列 API、自动循环切换、点击后最新消息摘要一致性校验；这些仍属于后续 M5.5+。

## 8. RPA 执行设计

### 8.1 基本执行原则

- 只操作前台可见微信窗口。
- 不并发操作多个会话。
- 不并发操作多个微信号。
- 每个动作都必须有前置校验。
- 每次发送前必须有审核停顿。
- 每次发送后必须等待最小发送间隔。
- 员工可随时暂停、终止或接管。

### 8.2 消息发送动作序列

```text
定位目标会话
↓
鼠标点击目标会话
↓
校验当前会话名称
↓
定位输入框
↓
鼠标点击输入框
↓
键盘输入待发送内容
↓
校验输入框内容
↓
进入发送前审核停顿
↓
员工未终止
↓
点击发送按钮或触发发送快捷键
↓
等待最小发送间隔
↓
记录动作日志
```

### 8.3 好友申请处理动作序列

```text
定位好友申请入口
↓
鼠标点击好友申请入口
↓
识别待处理申请列表
↓
按顺序选择一条申请
↓
鼠标点击通过按钮
↓
等待聊天窗口打开
↓
鼠标点击输入框
↓
键盘输入欢迎语
↓
校验输入框内容
↓
发送前审核停顿
↓
点击发送按钮或触发发送快捷键
↓
记录处理结果
```

### 8.4 动作配置

| 参数 | 说明 |
| --- | --- |
| InputMode | 键盘逐字输入 / 剪贴板粘贴后校验 |
| SendTriggerMode | 点击发送按钮 / 键盘快捷键 |
| ClickWaitMs | 鼠标点击后等待时间 |
| KeyboardWaitMs | 键盘输入后等待时间 |
| ReviewDelaySeconds | 发送前审核停顿 |
| MinSendIntervalSeconds | 最小发送间隔 |

MVP 当前真实微信测试默认：

- `InputMode = ClipboardPaste`
- `SendTriggerMode = ClickSendButton`
- `ReviewDelaySeconds = 3`
- `MinSendIntervalSeconds = 8`
- `SendMode = InputOnly`（默认开发联调；真实发送验收显式改为 `RealSendTest` 或 `ProductionGuarded`）
- `InputOnlyAfterVerifyAction = ClearInput`（默认清空 InputOnly 草稿并复核）

`ClipboardPaste` 仍然只操作官方微信前台窗口：RPA 先点击输入框，再写入剪贴板并模拟 `Ctrl+V`，随后通过输入框 OCR 校验确认内容一致。保留 `KeyboardTyping` 用于逐字 Unicode 键盘输入验证；若测试发现短中文回复丢字或错字，应优先使用 `ClipboardPaste`。

### 8.5 异常停止条件

出现以下任一情况，当前任务必须停止或暂停：

- 微信窗口丢失。
- 微信窗口焦点变化。
- 会话名称识别失败。
- 当前会话与队列目标不一致。
- 输入框定位失败。
- 输入框内容校验失败。
- OCR 置信度低于阈值。
- AI 调用失败或超时。
- 风险等级为高。
- 员工移动鼠标、切换窗口或按下停止快捷键。

### 8.6 鼠标移动轨迹增强

- 在鼠标移动时，可添加随机轨迹、随机抖动、空闲随机移动等参数，方便人工进行监督。
- 可增加鼠标指针放大或轨迹提示，降低值守员工漏看风险。
- 该能力不纳入 M4，后续如需要由独立配置项控制开启、暂停和关闭。

## 9. AI 大模型接入设计

### 9.1 设计目标

- 支持 DeepSeek 和通义千问。
- 预留 OpenAI Compatible 供应商适配。
- 统一模型调用接口。
- 不让业务代码直接依赖具体供应商。
- 支持不同场景选择不同模型。
- 支持结构化输出。
- 支持调用日志、Token 统计和失败降级。

### 9.2 模型调用接口

```text
ILlmProvider
├─ DeepSeekProvider
├─ TongyiProvider
└─ OpenAICompatibleProvider
```

核心方法：

```csharp
Task<LlmResponse> CompleteAsync(LlmRequest request, CancellationToken cancellationToken);
```

`LlmRequest` 关键字段：

| 字段 | 说明 |
| --- | --- |
| Scenario | 调用场景 |
| ModelName | 模型名称 |
| PromptTemplateCode | Prompt 模板 |
| UserInput | 客户问题 |
| Context | 知识库上下文 |
| Temperature | 随机性 |
| MaxTokens | 最大输出 |
| TimeoutSeconds | 超时时间 |

`LlmResponse` 关键字段：

| 字段 | 说明 |
| --- | --- |
| Success | 是否成功 |
| Intent | 意图 |
| Confidence | 置信度 |
| RiskLevel | 风险等级 |
| ReplyContent | 回复内容 |
| SourceRefs | 引用来源 |
| NeedHumanReview | 是否需要人工处理 |
| TokenUsage | Token 使用量 |

### 9.3 AI 调用场景

| 场景 | 说明 | MVP |
| --- | --- | --- |
| IntentRecognition | 意图识别 | 必做 |
| ReplyGeneration | 客服回复生成 | 必做 |
| RiskReview | 风险复核 | 可选增强 |
| WelcomeMessageGeneration | 欢迎语生成 | 必做 |
| ContentDraftGeneration | 朋友圈文案生成 | 可选 |
| Rewrite | 回复改写 | 可选 |

### 9.4 Prompt 管理

Prompt 不写死在代码中，统一存储为模板。

模板需要包含：

- 角色定义。
- 店铺信息。
- 商品知识。
- FAQ 命中内容。
- 售后规则。
- 风险禁止项。
- 回复语气。
- 输出 JSON Schema。

输出必须结构化，例如：

```json
{
  "intent": "PriceInquiry",
  "confidence": 0.91,
  "riskLevel": "Low",
  "replyContent": "可以的亲，这款目前活动价是...",
  "sourceRefs": ["FAQ:价格说明", "Product:商品A"],
  "riskWarnings": [],
  "needHumanReview": false
}
```

### 9.5 失败降级

AI 调用失败时：

```text
第一次失败
↓
按配置重试
↓
仍失败
↓
如果 FAQ 精确命中：使用 FAQ 标准答案并进入审核停顿
↓
否则：转人工，不自动发送
```

结构化解析失败、超时、API Key 无效时，一律不得自动发送。

## 10. 知识库与 RAG 设计

### 10.1 知识类型

| 类型 | MVP | 说明 |
| --- | --- | --- |
| 商品知识 | 必做 | 商品名称、卖点、规格、价格、适用人群、使用方式 |
| FAQ | 必做 | 高频问答、标准答案、相似问法 |
| 售后规则 | 必做 | 退换货、破损、赔偿、投诉处理边界 |
| 直播活动话术 | 建议 | 直播场次、活动、赠品、限时政策 |
| 风险规则知识 | 建议 | 禁用词、禁止承诺、敏感表达 |
| 朋友圈素材 | 可选 | 文案生成素材 |

### 10.2 知识处理流程

```text
后台录入知识
↓
保存 KnowledgeDocument
↓
拆分 KnowledgeChunk
↓
提取关键词
↓
生成 Embedding
↓
写入向量存储
↓
标记索引同步完成
```

### 10.3 混合检索流程

```text
客户问题
↓
意图识别和商品识别
↓
关键词检索
↓
向量检索
↓
合并候选知识
↓
按商品、分类、有效期、启停状态过滤
↓
计算综合分
↓
返回 TopN 知识片段
```

综合分建议：

```text
FinalScore = KeywordScore * 0.4 + VectorScore * 0.5 + BusinessBoost * 0.1
```

MVP 可配置权重，默认值后续根据测试调整。

### 10.4 自动发送知识门槛

低风险自动发送必须同时满足：

- 命中启用中的知识。
- 知识未过有效期。
- 知识命中分数达到阈值。
- 意图置信度达到阈值。
- 风险检测通过。
- AI 输出结构化解析成功。

未命中知识时：

- 不得自动发送。
- 记录知识缺口。
- 生成补充资料建议。
- 可提示员工人工处理。

### 10.5 向量存储方案

MVP 推荐两种兼容方案：

| 方案 | 优点 | 风险 |
| --- | --- | --- |
| PostgreSQL + 应用层向量计算 | 部署简单，适合小知识库 | 数据量大后性能有限 |
| PostgreSQL + 独立向量库 | 检索能力更强，可平滑扩展 | 多一个基础设施组件 |

技术设计预留独立向量库接口：

```text
IVectorStore
├─ InDatabaseVectorStore
└─ ExternalVectorStore
```

MVP 如果知识量不大，可以先用数据库存储向量并在应用层计算；后续再迁移到独立向量检索服务。

## 11. 风控设计

### 11.1 风险来源

- 客户原始消息。
- OCR 识别结果。
- AI 生成回复。
- 知识库内容。
- 发送目标会话。
- RPA 动作状态。

### 11.2 风控规则

高风险意图：

- 退款。
- 赔偿。
- 投诉。
- 差评威胁。
- 法律纠纷。
- 质量事故。
- 情绪激烈。

高风险回复：

- 承诺退款。
- 承诺赔偿。
- 承诺补发。
- 绝对化宣传。
- 功效承诺。
- 平台规避类表达。

### 11.3 自动发送决策

```text
低风险意图
AND 意图置信度达标
AND 知识命中达标
AND AI 输出解析成功
AND 风险检测通过
AND 当前会话识别确认
AND 输入框内容校验通过
AND 员工未终止
= 允许发送
```

任一条件不满足，不得自动发送。

## 12. 数据库设计草案

核心表：

| 表 | 说明 |
| --- | --- |
| Employees | 员工 |
| EmployeeClientAccessPolicies | 员工客户端使用授权 |
| WeChatWorkAccounts | 微信工作号 |
| DeviceHosts | 物理主机 |
| VirtualDevices | Windows VM 实例 |
| RpaClientInstances | RPA 客户端实例 |
| AutomationFeatureConfigs | 自动化配置 |
| Products | 商品 |
| FaqItems | FAQ |
| AfterSaleRules | 售后规则 |
| RiskRules | 风险规则 |
| KnowledgeDocuments | 知识文档 |
| KnowledgeChunks | 知识片段 |
| EmbeddingRecords | 向量记录 |
| LlmProviderConfigs | 模型供应商配置 |
| PromptTemplates | Prompt 模板 |
| CustomerQuestions | 客户问题 |
| ReplySuggestions | AI 回复建议 |
| RpaTasks | RPA 任务 |
| ConversationQueueItems | 会话队列 |
| RpaActionSteps | RPA 动作步骤 |
| RpaActionLogs | RPA 动作日志 |
| AiRequestLogs | AI 调用日志 |
| KnowledgeSearchLogs | 知识检索日志 |

MVP 暂不做复杂多租户 SaaS，但数据库字段应预留 `TenantId`，方便后续扩展。

设备相关表关系：

```text
DeviceHost
↓ 1:N
VirtualDevice
↓ 1:1
RpaClientInstance
↓ 1:1
Employee + WeChatWorkAccount
```

### 12.1 EmployeeClientAccessPolicies

| 字段 | 说明 |
| --- | --- |
| Id | 授权 ID |
| EmployeeId | 员工 ID |
| AccessStatus | 启用、暂停、禁用 |
| ValidFrom | 授权开始时间 |
| ValidTo | 授权结束时间 |
| MaxDailyUsageMinutes | 每日最长使用分钟数，可为空 |
| MaxSessionMinutes | 单次最长会话分钟数，可为空 |
| DisabledReason | 禁用原因：离职、调岗、违规、到期等 |
| UpdatedBy | 最后修改人 |
| UpdatedAt | 最后修改时间 |

授权校验规则：

```text
员工登录 RPA 客户端
↓
校验员工状态
↓
校验 AccessStatus 是否启用
↓
校验当前时间是否在 ValidFrom / ValidTo 范围内
↓
校验是否超过每日使用时长或单次会话时长
↓
通过：允许继续
不通过：拒绝登录、停止任务或强制下线
```

### 12.2 DeviceHosts

| 字段 | 说明 |
| --- | --- |
| Id | 物理主机 ID |
| HostName | 主机名称 |
| LanIp | 局域网 IP |
| CpuInfo | CPU 信息 |
| MemoryGb | 内存容量 |
| MaxVmCount | 规划最大 VM 数量 |
| Status | 正常、维护、停用 |

### 12.3 VirtualDevices

| 字段 | 说明 |
| --- | --- |
| Id | VM ID |
| DeviceHostId | 所属物理主机 |
| VmName | 虚拟机名称 |
| AssignedEmployeeId | 绑定员工 |
| WeChatWorkAccountId | 绑定微信工作号 |
| CpuCores | 分配 CPU |
| MemoryGb | 分配内存 |
| Resolution | 桌面分辨率 |
| ScaleFactor | Windows 缩放比例 |
| Status | 在线、离线、暂停、维护 |

### 12.4 RpaClientInstances

| 字段 | 说明 |
| --- | --- |
| Id | RPA 客户端实例 ID |
| VirtualDeviceId | 所属 VM |
| ClientVersion | 客户端版本 |
| LastHeartbeatAt | 最后心跳时间 |
| CurrentTaskId | 当前任务 |
| Status | 在线、离线、运行中、暂停、异常 |

## 13. API 设计草案

### 13.1 管理后台 API

| API | 说明 |
| --- | --- |
| `GET /api/employees` | 员工列表 |
| `GET /api/employees/{id}/client-access` | 查看员工客户端授权 |
| `PUT /api/employees/{id}/client-access` | 更新员工客户端授权、有效期和使用时长 |
| `GET /api/wechat-accounts` | 微信工作号列表 |
| `GET /api/device-hosts` | 物理主机列表 |
| `GET /api/virtual-devices` | Windows VM 列表 |
| `GET /api/rpa-client-instances` | RPA 客户端实例列表 |
| `GET /api/automation-configs` | 自动化配置列表 |
| `PUT /api/automation-configs/{id}` | 更新自动化配置 |
| `GET /api/products` | 商品列表 |
| `GET /api/faqs` | FAQ 列表 |
| `GET /api/after-sale-rules` | 售后规则列表 |
| `GET /api/knowledge-documents` | 知识文档列表 |
| `POST /api/knowledge-documents/{id}/sync-index` | 同步知识索引 |
| `GET /api/rpa-tasks` | RPA 任务列表 |
| `GET /api/rpa-tasks/{id}` | RPA 任务详情 |
| `GET /api/logs/ai-requests` | AI 调用日志 |
| `GET /api/logs/rpa-actions` | RPA 动作日志 |

### 13.2 RPA 客户端 API

| API | 说明 |
| --- | --- |
| `POST /api/agent/login` | RPA 客户端登录 |
| `POST /api/agent/heartbeat` | 心跳 |
| `POST /api/agent/register` | 注册 RPA 客户端实例 |
| `GET /api/agent/access-policy` | 获取当前员工客户端授权状态 |
| `GET /api/agent/config` | 获取自动化配置 |
| `POST /api/agent/tasks` | 创建 RPA 任务 |
| `PUT /api/agent/tasks/{id}/status` | 上报任务状态 |
| `PUT /api/agent/tasks/{id}/result` | 回写 OCR、AI 回复和风险结果 |
| `POST /api/agent/tasks/{id}/queue-items` | 上报会话队列 |
| `POST /api/agent/tasks/{id}/action-logs` | 上报动作日志 |
| `POST /api/agent/ocr-result` | 上报 OCR 结果 |
| `POST /api/agent/generate-reply` | 请求 AI 回复 |

### 13.3 AI 与知识库 API

| API | 说明 |
| --- | --- |
| `POST /api/ai/recognize-intent` | 意图识别 |
| `POST /api/ai/generate-reply` | 回复生成 |
| `POST /api/ai/review-risk` | 风险复核 |
| `POST /api/knowledge/search` | 知识检索 |
| `POST /api/knowledge/index/rebuild` | 重建知识索引 |

## 14. 安全与隐私设计

### 14.1 敏感数据

- 不保存微信登录凭证。
- 不保存完整微信聊天截图。
- 默认只保存 OCR 文本、AI 回复、风险结果和动作日志。
- 异常截图必须脱敏后保存。
- AI API Key 通过环境变量或本地安全配置注入，不写入仓库，不在数据库明文保存。
- 日志中不得输出 API Key、Secret、完整身份证号、手机号、地址。

### 14.2 设备安全

- RPA 客户端需要员工登录。
- 设备首次使用需要绑定。
- 后端记录设备 ID、员工 ID、工作号 ID。
- 异常设备可在后台禁用。
- 员工客户端授权过期、暂停或禁用后，RPA 客户端不得启动新任务。
- RPA 客户端心跳时必须返回当前授权状态；发现授权失效时，客户端应停止任务并提示员工联系管理员。
- 员工离职后，管理员应在 Web 后台禁用员工账号和客户端授权。

### 14.3 操作审计

必须记录：

- 谁启动了任务。
- 哪个工作号执行。
- 执行了哪些动作。
- 识别到什么客户消息。
- AI 生成了什么回复。
- 引用了哪些知识。
- 是否自动发送。
- 为什么停止或转人工。

## 15. 开发里程碑

### 15.1 阶段一：项目骨架

- 创建 ASP.NET Core Web API。
- 创建 React 管理后台。
- 创建 .NET Windows RPA 客户端。
- 建立数据库基础模型。
- 打通员工、物理主机、Windows VM、RPA 客户端实例和微信工作号绑定。

### 15.2 阶段二：知识库与 AI

- 商品、FAQ、售后规则管理。
- 知识文档和知识片段。
- DeepSeek / 通义千问模型适配。
- Prompt 模板管理。
- 关键词 + 向量混合检索。
- AI 回复生成和风控。

### 15.3 阶段三：RPA MVP

- 微信窗口识别。
- OCR 识别。
- 会话列表扫描。
- 鼠标点击和键盘输入。
- 好友申请自动处理。
- 欢迎语自动发送。
- 单线程会话自动回复。
- 审核停顿和发送频率控制。
- 紧急停止。

### 15.4 阶段四：管理后台与审计

- 自动化配置页面。
- RPA 任务控制台。
- 会话队列页面。
- AI 调用日志。
- RPA 动作日志。
- 风险记录。
- 简单统计报表。

### 15.5 阶段五：测试与试运行

- 单 VM 单账号测试。
- 单物理主机多 VM 测试。
- 多员工多 VM 测试。
- 常见客户咨询测试。
- 高风险问题测试。
- OCR 异常测试。
- AI 超时测试。
- 发送频率和审核停顿测试。
- 局域网部署试运行。

## 16. 重点测试用例

| 测试项 | 预期 |
| --- | --- |
| 客户 A、B 同时来消息 | 按队列顺序处理，不并发发送 |
| 发送前员工按暂停 | 不发送，任务暂停 |
| OCR 识别失败 | 不发送，记录异常 |
| 会话名称不匹配 | 不发送，停止当前任务 |
| 知识库未命中 | 不自动发送，记录知识缺口 |
| AI 超时 | 不自动发送，可重试或转人工 |
| 退款赔偿问题 | 高风险，转人工 |
| 输入框内容不一致 | 不发送，记录异常 |
| 当前时间超出配置时间段 | 不启动任务 |
| 工作号不在配置范围 | 不启动任务 |
| 员工客户端授权已过期 | RPA 客户端拒绝登录或停止当前任务 |
| 员工被后台禁用 | 下一次心跳后 RPA 客户端强制停止 |
| 员工超过每日使用时长 | 不允许启动新任务 |
| 员工超过单次会话时长 | 当前任务完成当前安全步骤后暂停 |
| 同一物理主机两个 VM 同时运行任务 | 两个 VM 任务互不影响，日志隔离 |
| 某个 VM 断网或关闭 | 只影响该 VM，对其他 VM 无影响 |
| 某个 VM 分辨率或缩放变化 | 优先重新自动定位；自动定位失败时使用配置兜底或提示重新校准 |

## 17. 技术确认与待确认问题

当前已确认：

- 数据库使用 PostgreSQL，本机开发优先连接 Docker 中的 postgres 容器。
- Windows RPA 客户端 UI 使用 WPF。
- OCR 引擎使用 Windows 系统 OCR 优先、本地 PaddleOCR 兜底。
- M4 采用视觉自动定位优先、配置坐标兜底，配置坐标相对微信窗口客户区。
- M4.5.2 已升级为分辨率自适应布局检测：候选输入区上边界评分、聊天区/输入区不重叠校验、输入校验区必须在输入区内。
- M4 默认 `ReviewDelaySeconds = 3`，`MinSendIntervalSeconds = 8`。
- M4.5.10 默认 `SendMode = InputOnly` 且 `InputOnlyAfterVerifyAction = ClearInput`，只输入并校验回复，不点击发送，并在校验后清空输入框；真实发送验收必须显式改为 `RealSendTest` 或 `ProductionGuarded`，并且只允许在测试号和值守环境中验证。
- M4 调试阶段可开启 `EnableDebugCaptures` 保存 OCR 裁剪图，调通坐标后关闭。
- M4 调试阶段可开启 `EnableLayoutDebugCaptures` 保存布局标注截图到 `%LOCALAPPDATA%\AIChat\RpaClient\layout-captures`，用于确认候选输入区上边界、消息区、输入框和发送按钮坐标。
- M4.2 采用 YOLO / ONNX 做旁路视觉识别验证，使用 `Microsoft.ML.OnnxRuntime 1.28.0`，验证阶段不直接接管真实点击和发送。
- M4.2 默认 `EnableYoloLayoutValidation=false`，放入模型并开启后才生成 `%LOCALAPPDATA%\AIChat\RpaClient\yolo-captures` 对比截图。
- M4.5.1 已接入当前会话连续监听，默认 `EnableContinuousReply=false`，开启后通过“开始连续监听”按钮启动。
- M4.5.1 默认按 `VisualLatestMessage` + `ReplyGroupingMode=Combined` 策略启动：解析视觉消息列表，最新有效消息是客户消息时合并待回复客户消息组，最新有效消息是我方消息则等待。
- M4.5.1 不新增数据库表和后端 API，每轮回复继续创建 `ReplyMessage` 类型 `RpaTask`，`CustomerQuestion` 保存整组客户问题文本。
- M4.5.3 已接入 OCR + VLM 视觉复核，当前真实调试使用 `AllRecognizedMessages` 调用 `qwen2.5vl:7b` 复核本轮非系统气泡，VLM 失败时跳过本轮继续监听。
- M4.5.4 已接入窗口锁定：启动任务或连续监听时锁定目标微信窗口句柄，并在 UI / 日志展示监听屏幕、窗口标题、客户区坐标、显示器边界和 DPI。
- M4.5.8 当前正在真实微信识别准确性验收，重点覆盖长句、多段消息、内联表情、重复文本和缓存误复用。
- 向量存储 MVP 先预留字段，暂不启用 pgvector。

后续仍需确认：

- DeepSeek 和通义千问 API Key 由谁提供，以及是否需要区分测试 Key 和生产 Key。
- MVP 一台物理主机计划承载几个 Windows VM。
- 每个 Windows VM 的默认 CPU、内存、分辨率和缩放比例。
- 员工如何访问和监督自己的 VM：直接坐在主机前切换 VM，还是通过远程桌面访问。
- 使用哪种虚拟化方案：Hyper-V、VMware Workstation、VirtualBox 或其他。
- Windows VM 的系统授权和微信客户端安装维护由谁负责。
- M4.2 训练数据采集范围、截图脱敏规则和标注负责人。
- YOLO 模型第一版训练样本、标注质量和验收阈值。
- 单轮最多处理会话数默认值。
- 紧急停止快捷键。

## 18. 参考边界

- [腾讯软件许可及服务协议](https://game.qq.com/contract_software.shtml)
- [企业微信微信客服 API 概述](https://qiyeweixin.apifox.cn/doc-417793)

本技术设计继续保留后续迁移企业微信 / 微信客服官方 API 的能力，避免系统长期绑定个人微信 RPA。
