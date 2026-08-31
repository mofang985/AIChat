# M3 知识库与 AI 基础模块说明

## 1. 本轮范围

M3 已打通后端基础闭环：

- 商品资料、FAQ、售后规则、风险规则、Prompt 模板、AI Provider 配置的基础管理接口。
- 关键词检索服务，支持从商品、FAQ、售后规则、知识分块中召回引用来源。
- AI 回复建议接口，统一输出意图、置信度、风险等级、回复文本、知识引用和自动发送决策。
- AI 请求日志、知识检索日志、回复建议记录落库。
- 向量检索只预留 `EmbeddingRecord`、`VectorRef`、`EmbeddingModel` 等字段，本轮不启用 pgvector。
- Web 管理后台增加 M3 资源列表、新增、编辑、知识检索和生成回复建议页面骨架。

本轮仍不实现真实微信 OCR、真实鼠标键盘执行、真实微信发送和多会话队列。

## 2. 数据库变更

新增 migration：

```text
20260729081053_M3KnowledgeAiSchema
```

新增表：

- `products`
- `faq_items`
- `after_sale_rules`
- `knowledge_documents`
- `knowledge_chunks`
- `risk_rules`
- `reply_suggestions`
- `ai_request_logs`
- `knowledge_search_logs`
- `prompt_templates`
- `llm_provider_configs`
- `embedding_records`

影响范围：

- `Up` 只新增 M3 表、外键和索引。
- `Down` 会删除 M3 新增表，仅作为开发环境回滚使用。
- 不修改、不删除 M2 已有表和字段。

本机开发连接字符串示例：

```powershell
$env:ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=aichat;Username=postgres;Password=YOUR_PASSWORD"
```

## 3. 后端 API

知识库管理：

- `GET/POST/PUT /api/products`
- `GET/POST/PUT /api/faqs`
- `GET/POST/PUT /api/after-sale-rules`
- `GET/POST/PUT /api/risk-rules`
- `GET/POST/PUT /api/prompt-templates`
- `GET/POST/PUT /api/llm-provider-configs`

检索与 AI：

- `POST /api/knowledge/search`
- `POST /api/ai/reply-suggestions`
- `GET /api/ai/reply-suggestions`
- `GET /api/ai/request-logs`

## 4. AI Provider 配置

本轮默认使用 OpenAI Compatible 调用方式，DeepSeek、通义千问均可通过 Provider 配置接入：

| 字段 | 说明 |
| --- | --- |
| `ProviderCode` | 调用时传入的供应商编码，例如 `deepseek` |
| `ProviderType` | `OpenAICompatible` / `DeepSeek` / `Tongyi` |
| `BaseUrl` | 兼容接口基础地址，不包含 `/chat/completions` |
| `ModelName` | 模型名称 |
| `ApiKeyEnvironmentVariable` | API Key 环境变量名 |
| `TimeoutSeconds` | 单次调用超时时间 |

API Key 不进入数据库、不进入仓库。示例：

```powershell
$env:AIChat__Llm__DeepSeek__ApiKey="YOUR_DEEPSEEK_KEY"
```

后台配置中只填写环境变量名：`AIChat__Llm__DeepSeek__ApiKey`。

### 4.1 本地 Ollama 测试配置

本地临时测试可使用 Ollama 的 OpenAI Compatible 接口。当前开发数据库已配置：

| 字段 | 值 |
| --- | --- |
| `ProviderCode` | `ollama` |
| `ProviderType` | `OpenAICompatible` |
| `BaseUrl` | `http://localhost:11434/v1` |
| `ModelName` | `qwen2.5:7b` |
| `ApiKeyEnvironmentVariable` | `AIChat__Llm__Ollama__ApiKey` |
| `TimeoutSeconds` | `180` |

Ollama 本地接口不需要真实 API Key，但当前应用会校验 Key 非空，因此开发环境使用占位值：

```powershell
$env:AIChat__Llm__Ollama__ApiKey="ollama"
```

如果 RPA 或后端提示 `localhost:11434` 被目标计算机拒绝，说明本地 Ollama 服务没有监听或刚启动尚未就绪。可先检查：

```powershell
ollama serve
```

另开一个 PowerShell 验证模型服务是否可访问：

```powershell
Invoke-RestMethod http://localhost:11434/api/tags
```

能返回 `qwen2.5:7b` 等模型列表后，再重新发起 RPA 回复任务；如果后端 API 进程是在 Ollama 启动前运行的，也可以重启后端 API 后再测。

启动后端前还需要确保数据库连接字符串已设置：

```powershell
$env:ConnectionStrings__DefaultConnection="Host=localhost;Port=5432;Database=aichat;Username=postgres;Password=YOUR_PASSWORD"
```

RPA 客户端本地测试配置已指定 `ProviderCode=ollama`，并将 `SendMode=InputOnly`、`InputOnlyAfterVerifyAction=ClearInput`，先验证 AI 回复、输入链路和输入框 OCR 校验；校验通过后清空草稿，不直接点击发送。

### 4.2 AI 回复模式配置

后端回复建议接口支持通过 `src/AIChat.Api/appsettings.json` 配置回复来源策略。当前测试环境已直接配置为 `LlmOnly`：

```json
{
  "Ai": {
    "ReplyMode": "LlmOnly",
    "EnableLlmOnlyBusinessFactGuard": false
  }
}
```

可选值：

- `KnowledgeFirst`：默认模式。先执行知识库检索；有命中时使用知识库增强提示词生成回复；无命中时只允许寒暄、确认、认可、轻量闲聊等低风险短回复，业务事实问题仍转人工。
- `LlmOnly`：测试/研究模式。跳过知识库检索，直接把客户最后一句和双方聊天上下文交给已配置的大模型生成结构化回复；`KnowledgeRefsJson` 保存为空数组。

`LlmOnly` 只改变回复生成来源，不新增 API、不新增数据库、不修改 RPA 真实发送逻辑。价格、库存、物流、售后、赔付、商品参数、活动优惠等业务事实如果没有明确上下文依据，模型提示词要求 `ShouldAutoSend=false`。

`EnableLlmOnlyBusinessFactGuard` 是测试开关：

- `true`：默认安全策略。后端会按关键词兜底拦截价格、库存、物流、售后、商品参数等业务事实或承诺风险，避免自动发送。
- `false`：当前简短测试策略。只关闭 `LlmOnly` 的业务事实关键词兜底；仍保留 AI 结构化 JSON、`ShouldAutoSend=true`、最终 `RiskLevel=Low`、风险规则、RPA 输入框 OCR 校验、倒计时和 `SendMode` 真实发送安全门。

如果需要恢复默认知识库优先，可改为：

```json
{
  "Ai": {
    "ReplyMode": "KnowledgeFirst",
    "EnableLlmOnlyBusinessFactGuard": true
  }
}
```

也可以在当前 PowerShell 会话中用环境变量临时覆盖配置，不需要修改文件。PowerShell 写法不要放进 `appsettings.json`：

```powershell
$env:Ai__ReplyMode="LlmOnly"
dotnet run --project .\src\AIChat.Api\AIChat.Api.csproj
```

恢复默认知识库优先：

```powershell
$env:Ai__ReplyMode="KnowledgeFirst"
dotnet run --project .\src\AIChat.Api\AIChat.Api.csproj
```

配置为空或非法值时按 `KnowledgeFirst` 处理，并写入后端日志；若本次回复仍进入人工复核，失败原因中也会带出非法配置提示。

## 5. 自动发送决策

`/api/ai/reply-suggestions` 的保守规则：

- `KnowledgeFirst` 默认先查知识库；未命中时只允许无业务事实的低风险短回复。
- `LlmOnly` 跳过知识库，直接调用大模型；`EnableLlmOnlyBusinessFactGuard=true` 时业务事实/承诺类问题或回复由后端兜底转人工。
- AI Provider 未配置或未启用：保存失败回复记录，`ShouldAutoSend=false`。
- API Key 缺失：保存失败回复记录，`ShouldAutoSend=false`。
- AI 调用失败、超时、结构化解析失败：保存失败回复记录，`ShouldAutoSend=false`。
- 命中高风险规则或 AI 返回非低风险：进入人工复核，`ShouldAutoSend=false`。
- 只有结构化解析成功、最终风险为 `Low`、AI 允许且当前回复模式的来源安全规则通过时，才会返回 `ShouldAutoSend=true`。

M3 只生成可审计的回复建议，不触发真实微信发送。后续 M4 的 RPA 客户端需要在发送前继续保留员工监督停顿和终止能力。

## 6. 验证结果

已验证：

```powershell
dotnet build AIChat.slnx
dotnet test AIChat.slnx
npm run build --prefix src/AIChat.Web
dotnet ef database update --project src/AIChat.Infrastructure --startup-project src/AIChat.Api
```

集成验证已覆盖：

- 创建测试商品和 FAQ。
- 调用 `/api/knowledge/search` 返回知识引用。
- 创建缺失 API Key 的测试 Provider。
- 调用 `/api/ai/reply-suggestions` 保存失败回复建议，且 `ShouldAutoSend=false`。

## 7. 后续阶段

M4 已进入单会话 RPA 闭环，重点不是多会话和好友申请，而是先打通一个微信会话：

```text
员工点击开始任务
↓
识别微信窗口
↓
截图聊天区并 OCR 最后一条客户消息
↓
调用 M3 回复建议接口
↓
低风险时模拟点击输入框和键盘输入
↓
发送前停顿，允许员工终止
↓
发送或人工复核
↓
回传动作日志
```

M4 后下一步进入 M5：多会话队列。在 M5 中再处理未读会话扫描、队列排序、单线程顺序切换和异常跳过策略。
