# AIChat — 个人微信工作号有人值守 RPA·AI 客服系统

面向店内多名员工个人微信工作号的「有人值守 + 全视觉 RPA + AI + 知识库」客服助手：通过截图、OCR 与图像识别操作官方微信 Windows 客户端，自动处理客户消息并生成低风险 AI 回复，全程保留人工审核停顿、单线程队列、发送频率控制与人工接管能力。

> 合规边界：严格「全视觉」——仅使用截图 / OCR / 鼠标键盘模拟操作官方微信前台窗口，不读取微信数据库，不使用协议、Hook、插件或多开器。

## 核心能力

- **AI 自动回复闭环**：窗口定位 → 布局检测 → 气泡 OCR（可疑时 VLM 复核）→ 视觉消息流解析 → 后端生成回复建议（知识检索 + 意图识别 + 风控）→ 仅 `ShouldAutoSend=true` 且 `RiskLevel=Low` 时发送 → 发送前审核倒计时 → 发送后校验，动作全程落库审计。
- **连续监听**：轮询当前会话，合并连续客户消息为待回复组，生成综合回复；画面指纹与气泡 hash 缓存去重，达到会话时长 / 回复次数上限或员工暂停时安全停止。
- **未读队列受控切换（只读安全边界）**：识别未读角标与候选会话行，稳定性预演后仅切换会话，不输入、不发送、不调用 AI。
- **知识库与 RAG**：商品、FAQ、售后规则、风险规则、知识文档分片与关键词检索；`KnowledgeFirst` 模式先检索知识库，未命中只允许低风险寒暄类回复，业务事实一律转人工。
- **风控与发送安全**：敏感词 / 高风险意图检测；`SendMode` 默认 `InputOnly + ClearInput` 安全干跑（只输入校验、不点击发送），真实发送需显式切换 `RealSendTest` / `ProductionGuarded`。
- **YOLO/ONNX 视觉旁路验证**：本地 `OnnxRuntime` 推理识别微信界面 7 类区域，与 OpenCV 布局检测并行比对，只出调试图不接管点击。
- **管理后台**：员工与客户端授权、工作号 / 主机 / VM、自动化配置中心、RPA 任务控制台、知识库维护、AI 模型配置、日志审计与统计。

## 架构

```
┌─────────────┐   截图/OCR/视觉识别/拟人输入    ┌──────────────────┐
│ 微信 Windows │ ◄──────────────────────────── │ AIChat.RpaClient │
│  官方客户端   │                               │  (WPF, 每 VM 一个) │
└─────────────┘                               └───────┬──────────┘
                                                      │ Agent API
                                  ┌───────────────────▼───────────────┐
                                  │      AIChat.Api (ASP.NET Core)     │
                                  │  Application / Domain / Infrastructure │
                                  │  EF Core + PostgreSQL              │
                                  └───────────────┬───────────────────┘
                                                  │ REST API
                                        ┌─────────▼─────────┐
                                        │ AIChat.Web (React) │ 管理后台
                                        └───────────────────┘
```

- **后端**：ASP.NET Core Web API，Clean Architecture 四层（`AIChat.Api` / `AIChat.Application` / `AIChat.Domain` / `AIChat.Infrastructure`），EF Core + PostgreSQL，OpenAI 兼容 `ILlmProvider`（DeepSeek / 通义千问 / Ollama）。
- **RPA 客户端**：.NET 10 WPF（`AIChat.RpaClient`），Windows OCR + PaddleOCR 双引擎、Ollama VLM 视觉复核、`OnnxRuntime` YOLO 推理、OpenCV 布局检测、鼠标键盘自然交互仿真。
- **管理后台**：React 19 + TypeScript + Vite（`AIChat.Web`）。
- **视觉训练工具**：Python + Ultralytics YOLO 独立训练（`tools/AIChat.VisionTrainer`），采集 → 标注 → 训练 → 导出 ONNX → 主动学习 → 模型转正，带 Tkinter GUI。
- **测试**：`AIChat.UnitTests`（23 个 xunit 测试类，含 RPA 纯逻辑链接测试）、`AIChat.IntegrationTests`。

## 仓库结构

```
AIChat.slnx
├─ src/
│  ├─ AIChat.Api/             # Web API（minimal API，M2/M3 端点）
│  ├─ AIChat.Application/     # 应用层：AI、RPA 任务、风控、知识库、访问控制
│  ├─ AIChat.Domain/          # 领域层：实体、枚举
│  ├─ AIChat.Infrastructure/  # EF Core DbContext + 迁移、LLM Provider
│  ├─ AIChat.RpaClient/       # WPF 客户端（自动化核心）
│  └─ AIChat.Web/             # React 管理后台
├─ tests/
│  ├─ AIChat.UnitTests/
│  └─ AIChat.IntegrationTests/
├─ tools/
│  └─ AIChat.VisionTrainer/   # Python YOLO 视觉训练工具
├─ docker-compose.postgres.yml
└─ 文档/ *.md                 # 需求、设计、各里程碑说明
```

## 快速开始

### 1. 数据库（PostgreSQL）

```bash
cp .env.example .env        # 修改密码等敏感项
docker compose -f docker-compose.postgres.yml --env-file .env up -d
dotnet ef database update --project src/AIChat.Api
```

连接串通过环境变量 `ConnectionStrings__DefaultConnection` 注入。

### 2. 后端 API

```bash
dotnet run --project src/AIChat.Api
```

需配置大模型 API Key（OpenAI 兼容，任选其一）：

```bash
AIChat__Llm__DeepSeek__ApiKey=xxx
AIChat__Llm__Tongyi__ApiKey=xxx
# 或本地测试：
AIChat__Llm__Ollama__ApiKey=ollama   # http://localhost:11434/v1, qwen2.5:7b
```

### 3. 管理后台

```bash
cd src/AIChat.Web
npm install
npm run dev        # VITE_API_BASE_URL 指向后端地址
```

### 4. RPA 客户端（Windows）

```bash
dotnet run --project src/AIChat.RpaClient
```

配置见 `src/AIChat.RpaClient/appsettings.json`（支持 `AICHAT_RPA_` 前缀环境变量覆盖）。客户端会注册到后端并开启心跳；启动任务前请确认后端已配置客户端授权。

> 真实发送验收前提：显式切换 `SendMode=RealSendTest` 或 `ProductionGuarded`，使用测试微信号与测试会话，且员工在 VM 前值守。

### 5. 构建与测试

```bash
dotnet build AIChat.slnx
dotnet test AIChat.slnx
```

## 模块演进

| 模块 | 内容 | 状态 |
|---|---|---|
| M1 | 项目骨架（后端 / 前端 / RPA / 测试） | ✅ 已完成 |
| M2 | 设备与任务基础模块（员工授权、工作号、主机 / VM、RPA 实例、任务、心跳、动作日志） | ✅ 已完成 |
| M3 | 知识库与 AI 基础模块（商品 / FAQ / 售后 / 风险规则、Prompt、LLM Provider、回复建议与日志） | ✅ 已完成 |
| M4 | 单会话 RPA 自动回复闭环 | 代码闭环完成，真实微信验收中 |
| M4.2 | YOLO / ONNX 视觉识别旁路验证（v2 模型 mAP50≈0.9949） | 进行中 |
| M4.3 | YOLO 优先 / OpenCV 兜底切换 + 主动学习样本沉淀 | 待 M4.2 稳定后进入 |
| M4.5.x | 连续监听、布局引擎 2.0、VLM 复核、多屏锁定、性能缓存、发送安全收口 | 已接入，验收中 |
| M5.x | 未读队列只读扫描与受控切换 → 多会话单线程队列 | M5.1-M5.4 已接入 |
| M6-M8 | 好友申请自动处理 / 管理后台完善 / 多 VM 试运行 | 待开始 |

设计原则贯穿始终：**有人值守 + 全视觉 + 发送安全门只降级不放宽**。

## 相关文档

| 文档 | 说明 |
|---|---|
| `个人微信工作号AI辅助客服MVP需求文档.md` | 产品需求（v0.1） |
| `个人微信工作号有人值守RPA_AI客服技术设计文档.md` | 技术设计（v0.5） |
| `个人微信工作号有人值守RPA_AI客服开发实施计划.md` | 实施计划与里程碑（v0.5） |
| `M2设备与任务基础模块说明.md` / `M3知识库与AI基础模块说明.md` | 模块说明 |
| `M4单会话RPA自动回复闭环说明.md` / `M4.5单会话连续自动回复说明.md` | 闭环与连续监听说明 |
| `M4.2独立YOLO视觉训练工具技术文档.md` / `M4.2_YOLO_ONNX视觉识别验证计划.md` | YOLO 训练与验证 |
| `M4.5.3_OCR_VLM视觉复核说明.md` / `M4.5.6_AI自动发送准入策略修复说明.md` | 专项说明 |
| `AIChat.VisionTrainer_GUI与YOLO学习补录技术文档.md` | 训练工具 GUI 文档 |
| `电商AI客服系统产品需求文档.md` | 远期产品愿景（多平台演进蓝图） |

## 环境要求

- .NET SDK 10.0.201（见 `global.json`）
- PostgreSQL（本地或 Docker）
- Windows 10/11（RPA 客户端，需官方微信 Windows 客户端）
- Python 3.11+（仅 VisionTrainer 工具）
- 大模型 API Key（DeepSeek / 通义千问 / Ollama 任一）

## 安全与隐私

- LLM API Key 仅通过环境变量注入，不落库。
- 截图 / OCR 处理在店内局域网内完成，默认不保存完整截图，调试截图仅本机保存。
- 自动发送三重门槛：后端风控（`RiskLevel=Low`）+ 发送策略（`ShouldAutoSend`）+ 客户端 `SendMode` 安全收口。
