# M4 / M4.5 / M5.1 / M5.2 / M5.3 / M5.4 RPA 自动回复闭环与未读队列受控切换说明

## 1. 本轮范围

M4 已打通有人值守场景下的单个微信会话自动回复闭环，M4.5 在此基础上增加当前会话连续监听，M4.5.1 再把回复输入从“最新客户消息”升级为“待回复客户消息组”，M4.5.2 进一步把微信布局检测升级为分辨率自适应候选评分引擎。M5.1 在不改变单会话自动回复安全边界的前提下，新增左侧可见数字未读会话只读队列展示；M5.2 在候选行上只读 OCR 会话名、摘要、时间和未读数字；M5.3 增加连续扫描稳定性预演；M5.4 增加首个稳定候选受控切换：

- 员工先打开官方微信 Windows 客户端，并进入目标客户会话。
- RPA 客户端在授权有效时允许点击“开始任务”。
- 客户端通过窗口标题定位微信窗口，先截取微信客户区全图，再按界面结构自动分割会话列表、聊天内容区和底部输入区。
- M4.5.2 中，底部输入区不再只依赖单条水平线识别，而是生成多个候选输入区上边界，再综合横线覆盖率、输入区白底比例、输入区高度、发送按钮位置、聊天区有效高度和消息气泡数量评分。
- 基于分割后的聊天内容区优先执行视觉消息流解析：识别左侧客户气泡、右侧我方绿色气泡和居中系统提示，生成带发送方的消息列表。
- M4.5.1 判断是否回复时，不再依赖整块 OCR 文本顺序；最新有效消息是客户消息时，会合并末尾连续客户消息组后进入 AI 回复，最新有效消息是我方消息则等待客户下一条消息。
- 当前客户气泡 OCR 使用原图、聚焦裁剪图和文本增强图择优识别；调试模式下会保存每条气泡的 OCR 裁剪图，便于核对小字号短句是否被误识别。
- 自动定位失败或置信度不足时，`CoordinateMode=AutoWithManualFallback` 可回退 `appsettings.json` 中的配置坐标；`CoordinateMode=AutoOnly` 下直接停止，不使用低置信度布局继续发送。
- 优先使用 Windows 系统 OCR 识别客户消息文本，失败或为空时回退本地 PaddleOCR。
- 调用后端 `/api/ai/reply-suggestions` 获取 AI 回复建议和风控结果。
- 后端可通过 `Ai:ReplyMode` 控制回复来源：默认 `KnowledgeFirst` 先查知识库；测试阶段可改为 `LlmOnly`，让大模型直接根据待回复客户消息组和双方聊天上下文生成自然回复。
- 仅当 AI 返回 `ShouldAutoSend=true` 且 `RiskLevel=Low` 时，才点击输入框并输入回复；当前真实微信测试默认使用 `ClipboardPaste`，即写入剪贴板后模拟 `Ctrl+V`，避免逐字 Unicode 键盘输入在微信 / 输入法环境下偶发丢字或错字。仍可配置为 `KeyboardTyping` 做逐字键盘输入验证。
- `KnowledgeFirst` 未命中知识库时仍只允许寒暄、确认、认可、轻量闲聊等低风险短回复；`LlmOnly` 默认会对价格、库存、物流、售后、赔付、商品参数、活动优惠等业务事实或承诺风险做关键词兜底转人工。当前简短测试可临时设置 `Ai:EnableLlmOnlyBusinessFactGuard=false` 放开该关键词兜底，但其它安全门仍保留。
- 输入后再次 OCR 校验输入框区域；校验采用容错相似度，允许 OCR 少量漏字，但截错区域或识别为空仍不发送。
- 发送前进入审核倒计时，员工可暂停或紧急停止。
- `SendMode=RealSendTest` 或 `ProductionGuarded` 时，倒计时结束后点击发送按钮，再 OCR 校验输入框是否清空；只有确认输入框不再保留回复内容，才标记发送成功并回传任务状态、OCR、AI 回复、风险结果和动作日志。
- 员工可点击“开始连续监听”，RPA 会先解析当前聊天区消息列表；如果最新有效消息是客户消息，则提取待回复客户消息组并立即处理，如果最新有效消息是我方消息，则等待后续新客户消息。每一轮回复仍复用 M4 的完整 OCR、AI、风控、输入、发送模式、倒计时、真实发送和发送后校验链路。
- 员工可点击“扫描未读队列（只读）”，RPA 只读取左侧可见会话列表，先识别带数字的未读角标，再 OCR 候选行的会话名、最新消息摘要、时间和未读数字，连续扫描比对候选指纹和行位置并刷新本地 UI 队列；员工也可点击“切换首个可切换候选（受控）”，RPA 会重新扫描复核首个稳定候选，点击会话行后只校验右侧聊天标题，不输入、不发送、不调用 AI。

M4/M4.5 自动回复仍只处理员工当前打开的一个微信会话，不实现好友申请、欢迎语、朋友圈或后台无人值守调度。M5.1/M5.2/M5.3 只读扫描会展示左侧可见数字未读候选队列、OCR 文本信息和预演状态；M5.4 只允许员工显式触发首个稳定候选受控切换，点击后校验标题，但不输入会话内容、不发送、不调用 AI 回复、不创建后端回复任务。自然交互仿真只用于研究真实桌面自动化链路的可靠性，不改变 AI 风控、发送前倒计时、输入校验和 `SendMode` 真实发送安全门。

## 2. 后端变更

M4 不新增数据库表，也不新增 migration。复用现有 `RpaTask`、`RpaActionLog`、`ReplySuggestion`。

回复建议仍复用 M3 的 `/api/ai/reply-suggestions`，不新增后端 API。`Ai:ReplyMode=KnowledgeFirst` 为默认生产安全策略；`Ai:ReplyMode=LlmOnly` 用于测试本地/外部大模型的自然对话能力，`KnowledgeRefsJson` 为空数组。`Ai:EnableLlmOnlyBusinessFactGuard=false` 只用于临时测试简短回复，不建议生产启用；真实发送仍必须同时满足后端和 RPA 侧安全门。

新增 RPA 任务结果回写接口：

```text
PUT /api/agent/tasks/{id}/result
```

请求字段：

- `ConversationKey`
- `CustomerDisplayName`
- `IncomingMessageText`
- `AiReplyText`
- `RiskResult`

规则：

- 无效任务 ID 返回 `404`。
- 空字符串、空白字符串和 `null` 不覆盖任务已有字段。
- 非空字段会 `Trim` 后写入对应任务结果字段。

## 3. RPA 客户端变更

客户端版本升级为：

```text
0.4.5.5-m455
```

新增模块：

- `RpaBackendClient`：封装注册、心跳、任务、日志、AI 回复建议等后端调用。
- `WeChatWindowLocator`：基于窗口标题关键字定位微信窗口，并取得客户区坐标。
- `ScreenCaptureService`：按屏幕坐标截图并输出 PNG。
- `WeChatLayoutDetector`：基于微信客户区截图自动分割会话列表、聊天内容区、底部输入区，并定位 OCR 消息区、输入校验区和发送按钮；M4.5.2 内部使用候选布局生成、评分和几何安全校验，失败时按坐标模式决定是否回退配置坐标。
- `WeChatWindowLocator`：M4.5.4 已增强为“启动时锁定目标窗口”，记录微信窗口句柄、标题、客户区坐标、显示器边界和 DPI，连续监听过程中优先复用锁定窗口，避免多屏或多个微信窗口时误切换目标。
- `YoloOnnxVisionDetector`：M4.2 旁路视觉验证模块，使用本地 ONNX 模型识别微信关键区域，只输出日志和对比调试图，不接管真实点击。
- `PaddleOcrEngine`：优先调用 Windows 系统 OCR，失败或为空时回退本地 PaddleOCR 中文模型。
- `MouseKeyboardExecutor`：通过 Windows API 执行鼠标点击和 Unicode 键盘输入，并按配置执行自然交互仿真；关闭 `HumanizeInput` 后恢复直接定位和固定间隔的旧行为。
- `ChatMessageVisualExtractor`：M4.5 视觉消息流解析模块，输出客户 / 我方 / 系统 / 未知消息列表，用于判断是否需要回复。
- `PaddleOcrEngine`：对微信小气泡文本增加文本增强裁剪和放大识别候选，降低短句如“你好”和问号被误识别的概率。
- `CustomerMessageGroup`：M4.5.1 待回复客户连续消息组，默认用于生成一条综合回复。
- `SingleConversationReplyCycleExecutor`：编排可复用的单次回复闭环。
- `SingleConversationTaskRunner`：保留 M4 单次“开始任务”入口，内部调用单次回复执行器。
- `ContinuousConversationTaskRunner`：编排 M4.5 当前会话连续监听、视觉消息流解析、轮询、消息合并、去重和停止条件。
- `ContinuousConversationTaskRunner` 在 M4.5.5 中新增性能优化：连续监听布局缓存、底部最近气泡优先 OCR、复用预识别窗口/布局/消息流，避免触发回复时重复识别。
- `CustomerMessageExtractor` / `ContinuousConversationState`：生成客户消息快照、消息指纹，抑制重复回复并记录连续回复状态。

新增 UI 展示：

- 当前任务状态。
- OCR 客户消息。
- AI 回复。
- 风险结果。
- 坐标模式：视觉自动定位 / 配置坐标兜底 / 定位失败。
- 发送倒计时。
- 最后异常原因。
- 连续监听状态、连续回复次数、最近轮询时间、最新消息发送方、最新消息文本、合并等待倒计时和连续失败次数。
- M4.5.4 增加当前监听目标展示：窗口标题、窗口句柄、客户区坐标、显示器边界和 DPI。
- M4.5.5 增加真实执行阶段展示：正在定位窗口 / 正在布局检测 / 布局缓存命中 / 正在识别底部最近气泡 / 正在 OCR 气泡 / 正在 VLM 复核气泡 / 正在生成 AI 回复。

## 4. 视觉自动定位与坐标兜底

配置位于 `src/AIChat.RpaClient/appsettings.json`。M4 当前真实发送验收使用 `CoordinateMode=AutoOnly`：

- 先截图整个微信客户区。
- 使用 OpenCV 先识别左侧会话列表与右侧聊天区的主分割线，再按微信左侧导航栏宽度估算会话列表左边界，生成覆盖完整可见会话列表列的 `ConversationListRegion`。
- 自动生成类似调试图中的三类大区域：红色会话列表、蓝色聊天内容区、黄色输入区；红色会话列表应覆盖搜索框、头像、昵称、摘要、时间和未读角标，不只覆盖单条候选行。
- 客户消息 OCR 只在蓝色聊天内容区内部进一步裁剪左侧客户气泡，避免把左侧会话列表的昵称、时间和摘要识别进来；完整聊天上下文 OCR 会覆盖聊天内容区左右两侧，用于包含客户消息和自己已发送消息。
- 发送按钮优先按绿色按钮区域识别；若输入框为空导致按钮不明显，则尝试通过 Windows OCR 识别底部右侧“发送”文字，再尝试底部按钮矩形检测和输入区位置推断。
- 自动定位结果达到 `LayoutDetectionMinConfidence` 且坐标都落在微信客户区内，才作为本次运行坐标。
- `CoordinateMode=AutoWithManualFallback` 时，自动定位失败会回退配置坐标。
- `CoordinateMode=Manual` 时直接使用配置坐标。
- `CoordinateMode=AutoOnly` 时自动定位失败即停止，不使用配置坐标兜底。

M4.5.2 布局检测 2.0 的安全规则：

- 输入区上边界候选来源包括长横线、发送按钮反推、输入区白底变化和保守比例兜底。
- 候选默认只在微信客户区高度 55% 到 92% 范围内搜索，输入区高度比例必须落在 8% 到 38%。
- `ConversationContextRegion` 只能覆盖聊天内容区，不能包含底部工具栏、输入框或左侧会话列表。
- `InputVerifyRegion` 必须完全包含在 `InputAreaRegion` 内，优先覆盖输入框主体区域。
- `ChatContentRegion.Bottom` 必须小于或等于 `InputAreaRegion.Top`，两者不得重叠。
- `layout-captures` 会额外绘制候选输入区上边界、候选来源和候选分数；红线为当前最高分候选，绿色为安全候选，灰色为未通过安全校验或低分候选。
- 发送后如果输入校验区 OCR 仍识别到明显非空文本，客户端会判定为发送后校验异常，不再直接标记成功。

配置坐标均相对微信窗口客户区。`X` / `Y` 为正数时从客户区左上角计算，为负数时从客户区右侧 / 底部反向计算，便于输入框和发送按钮适配不同窗口高度。

```json
{
  "ClientVersion": "0.4.5.5-m455",
  "Automation": {
    "WeChatWindowTitleKeyword": "微信",
    "CoordinateMode": "AutoOnly",
    "IncomingMessageRegion": { "X": 300, "Y": 70, "Width": 1300, "Height": 720 },
    "InputClickPoint": { "X": 520, "Y": -130 },
    "InputVerifyRegion": { "X": 300, "Y": -220, "Width": 1600, "Height": 200 },
    "SendButtonPoint": { "X": -55, "Y": -34 },
    "ReviewDelaySeconds": 3,
    "MinSendIntervalSeconds": 8,
    "OcrMinConfidence": 0.65,
    "SendMode": "InputOnly",
    "InputOnlyAfterVerifyAction": "ClearInput",
    "InputVerifyRetryCount": 1,
    "InputVerifyDelayMs": 300,
    "EnableKeyboardFallbackOnClipboardFailure": true,
    "EnableDebugCaptures": true,
    "DebugCaptureDirectory": "",
    "EnableLayoutDebugCaptures": true,
    "LayoutDebugCaptureDirectory": "",
    "LayoutDetectionMinConfidence": 0.65,
    "EnableYoloLayoutValidation": true,
    "YoloModelPath": "",
    "YoloLabelsPath": "",
    "YoloInputSize": 960,
    "YoloMinConfidence": 0.35,
    "YoloNmsThreshold": 0.45,
    "EnableYoloDebugCaptures": true,
    "YoloDebugCaptureDirectory": "",
    "EnableVisionOcrReview": true,
    "VisionOcrProvider": "Ollama",
    "VisionOcrBaseUrl": "http://localhost:11434",
    "VisionOcrModel": "qwen2.5vl:7b",
    "VisionReviewMode": "AlwaysForCustomerMessages",
    "VisionOcrTimeoutSeconds": 8,
    "VisionOcrMinConfidence": 0.70,
    "VisionOcrFailureBehavior": "SkipAndContinue",
    "EnableVisionOcrDebugCaptures": true,
    "VisionOcrDebugCaptureDirectory": "",
    "InputMode": "ClipboardPaste",
    "HumanizeInput": true,
    "MouseMoveDurationMsMin": 180,
    "MouseMoveDurationMsMax": 520,
    "MouseMoveStepsMin": 8,
    "MouseMoveStepsMax": 22,
    "MouseMoveJitterPixels": 3,
    "ClickJitterPixels": 6,
    "ClickDownMsMin": 45,
    "ClickDownMsMax": 120,
    "KeyPressMsMin": 25,
    "KeyPressMsMax": 75,
    "KeyDelayMsMin": 35,
    "KeyDelayMsMax": 120,
    "TypingPauseChance": 0.08,
    "TypingPauseMsMin": 180,
    "TypingPauseMsMax": 520,
    "EnableContinuousReply": true,
    "ContinuousPollIntervalSeconds": 3,
    "MessageMergeWindowSeconds": 5,
    "MaxContinuousSessionMinutes": 30,
    "MaxRepliesPerContinuousSession": 20,
    "MaxConsecutiveContinuousFailures": 3,
    "DuplicateMessageSuppressMinutes": 10,
    "ContinuousStartMode": "VisualLatestMessage",
    "ReplyGroupingMode": "Combined",
    "StopContinuousOnManualReviewRequired": false,
    "StopContinuousOnSendFailure": true,
    "EnablePerformanceDiagnostics": true,
    "ContinuousMaxVisualMessagesToOcr": 8,
    "ContinuousVisualOcrBottomRatio": 0.60,
    "ContinuousVisionReviewScope": "PendingCustomerGroupOnly",
    "EnableContinuousLayoutCache": true
  }
}
```

默认假设：

- 微信窗口最大化。
- VM 分辨率 1920x1080。
- Windows 缩放 100%。
- Windows OCR 简体中文识别语言可用。
- 一个 VM 内只运行一个官方微信 Windows 客户端和一个 RPA 客户端。

如果不同 VM 的窗口布局不一致，应先查看自动定位标注截图；只有自动定位长期失败时，再调整 `appsettings.json` 中的兜底坐标。

当前默认开发联调配置为 `SendMode=InputOnly`、`InputOnlyAfterVerifyAction=ClearInput`、`InputVerifyRetryCount=1`、`EnableKeyboardFallbackOnClipboardFailure=true`、`EnableYoloLayoutValidation=true`、`CoordinateMode=AutoOnly`：RPA 会输入并校验 AI 回复，不点击发送按钮；如果输入框 OCR 校验为空或未命中，会重新激活锁定微信窗口、清空并重试一次；如果剪贴板被占用导致粘贴失败，则退回逐字符键盘输入；最终校验通过后清空微信输入框再复核，避免留下可误发草稿。需要真实发送验收时，必须显式改为 `SendMode=RealSendTest` 或 `SendMode=ProductionGuarded`，并确认正在使用测试微信号和测试客户会话。
自然交互仿真默认开启：`HumanizeInput=true`。如需排查坐标或输入问题，可临时改为 `false`，让鼠标恢复直接移动、键盘恢复固定间隔输入。该配置只影响动作节奏和轨迹，不会绕过 `ShouldAutoSend=true`、`RiskLevel=Low`、发送前倒计时、输入框 OCR 校验和 `SendMode`。

2026-07-30 在 3862x2110 微信窗口调试时发现，旧版自动定位会错过底部输入区上边界并退回 1080p 手动兜底坐标，导致 `IncomingMessageRegion` 和 `InputVerifyRegion` 只覆盖窗口左上/底部局部。已修复输入区分割线搜索范围、`LayoutSplit` 置信度加分、左侧客户消息 OCR 区宽度和输入校验区域高度；真实发送验收期间保持 `AutoOnly`，避免旧兜底坐标影响发送安全。

2026-07-30 14:23 真实发送验收时发现短回复 `嗯嗯好的` 已经输入到微信输入框，但 `InputVerifyOcr` 调试截图中文本贴近截图左上边缘，Windows OCR 对 2-6 字短文本存在漏字/近似字误识别，导致输入框内容校验误判失败。已把输入校验截图区域向左上扩展留白，并对短回复校验增加少量漏字容错；截错区域、识别为空或相似度不足仍不发送。

`EnableDebugCaptures=true` 时，客户端会把 OCR 使用的裁剪区域保存到本机 `%LOCALAPPDATA%\AIChat\RpaClient\debug-captures`，并在运行日志里输出文件路径。运行日志会识别 `截图：*.png/.jpg/.jpeg/.bmp/.webp` 本地路径，员工可直接双击对应日志打开截图。该截图只保存裁剪区域，不保存完整屏幕；调通坐标后可改为 `false`。

`EnableLayoutDebugCaptures=true` 时，客户端会把微信客户区自动定位标注截图保存到本机：

```text
%LOCALAPPDATA%\AIChat\RpaClient\layout-captures
```

标注截图会显示本次识别到的会话列表、聊天内容区、底部输入区、OCR 消息区、输入校验区、输入点击点和发送按钮点。该截图只保存在本机，不上传后端，用于 M4 真实微信窗口调试。

2026-07-30 真实发送调试时发现，`IncomingMessageOcr` 调试截图只包含左侧客户气泡，未包含右侧自己已发送消息，容易误以为聊天上下文截取错误。当前已新增 `ConversationContextRegion` 和 `ConversationContextOcr`：布局图中会出现完整上下文框，运行日志会额外保存一张完整聊天内容区 OCR 调试图；AI 优先以视觉消息流中的待回复客户消息组作为本轮问题，上下文使用格式化后的双方消息列表。

2026-07-30 连续监听调试时发现，左侧客户消息 OCR 区可能同时识别到微信居中的系统提示，例如“你已添加了...现在可以开始聊天了”，导致最新客户消息误判。当前连续监听默认改为视觉消息流判断：居中时间和系统提示会标记为 `System` 并跳过，最新有效消息必须明确为客户消息才会触发回复。

2026-07-30 真实输入调试时发现，后端保存的 AI 回复为“好的，可以的。”，但微信输入框中可能因逐字 Unicode 键盘输入变成“好的，，以的。”。当前默认输入模式已调整为 `ClipboardPaste`：RPA 仍点击微信输入框并模拟键盘 `Ctrl+V`，随后继续执行输入框 OCR 校验、审核倒计时、真实发送和发送后校验。

2026-07-30 连续监听调试时发现，当左侧客户消息区同时存在上方旧消息“我是 Hzq”和下方新消息“你好”时，整块区域 OCR 可能只返回上方旧消息。当前已新增视觉消息流解析，从下往上判断最新有效消息；旧的底部向上重叠分片 OCR 仅作为单次回复调试兜底保留，连续监听默认不再用它触发回复。

2026-07-30 连续监听启动逻辑已升级为视觉消息流解析：点击“开始连续监听”时，不再只按完整聊天区 OCR 文本顺序判断是否已回复，因为微信左右气泡可能被 OCR 按列读取，导致右侧旧回复被误认为最新客户消息之后的回复。当前改为识别聊天区气泡列表，最新有效消息是客户消息才立即触发回复，最新有效消息是我方消息则等待客户下一条消息。

2026-07-30 M4.5.1 已升级为消息组回复：当客户连续发送多条消息且中间没有我方回复时，RPA 会合并末尾连续客户消息生成 `CustomerMessageGroup`，把整组问题作为 `CustomerQuestion` 传给后端 AI。默认 `ReplyGroupingMode=Combined`，即用一条综合回复覆盖整组问题，不逐条连续发送。

2026-07-30 真实微信 OCR 调试时发现，客户消息“鞋子 兄弟 不是衣服”可能被 Windows OCR 识别成“子兄弟不是衣服”，漏掉气泡首字，导致 AI 回复方向被带偏。当前视觉消息流中的客户气泡改为准确 OCR：同时对 Windows OCR 和 PaddleOCR 结果做质量比较，并优先选择“更完整且高度相似”的文本；客户/文本候选框左右留白也已加大，降低首字贴边漏识别概率。

2026-07-30 真实微信 OCR 调试时发现，客户短句“好的，我知道了，谢谢你”可能被识别成“好的，我首了，谢谢你”。当前 OCR 清洗阶段加入保守短语纠错，只修正常见确定性误识别，不对普通商品、价格、售后等业务文本做自由改写。

`EnableYoloLayoutValidation=true` 时，客户端会在现有 OpenCV 布局定位之后运行 M4.2 YOLO / ONNX 旁路验证。默认模型路径为：

```text
%LOCALAPPDATA%\AIChat\RpaClient\models\wechat-layout\wechat-layout.onnx
```

YOLO 对比调试截图保存到：

```text
%LOCALAPPDATA%\AIChat\RpaClient\yolo-captures
```

M4.2 只用于验证识别质量。模型缺失、未开启或推理失败时，只记录 `YoloLayoutValidationSkipped` / `YoloLayoutValidationFailed` 动作日志，不影响当前 M4 的 OCR、输入、审核倒计时和真实发送流程。

当前本机验收模型使用 `imgsz=960` 导出，RPA 配置中的 `YoloInputSize` 必须保持为 `960`。模型包已安装到默认目录，包含：

```text
%LOCALAPPDATA%\AIChat\RpaClient\models\wechat-layout\wechat-layout.onnx
%LOCALAPPDATA%\AIChat\RpaClient\models\wechat-layout\labels.txt
%LOCALAPPDATA%\AIChat\RpaClient\models\wechat-layout\model-version.json
```

## 4.1 M4.5 连续监听

M4.5 只监听员工当前打开的一个微信会话。它不会切换会话，不扫描左侧未读列表，也不处理好友申请。

启动规则：

- 默认 `EnableContinuousReply=false`，需要在 `appsettings.json` 中开启后，RPA 客户端才允许点击“开始连续监听”。
- 启动后先截图完整聊天内容区，并解析客户 / 我方 / 系统 / 未知消息列表。
- 默认按 `VisualLatestMessage` 策略启动：根据最新有效消息发送方决策，最新是客户消息则提取待回复客户消息组并立即处理，最新是我方消息则等待客户后续新消息。

监听规则：

- 按 `ContinuousPollIntervalSeconds` 定时轮询当前微信会话。
- 连续监听默认不使用左侧客户 OCR 作为回复判断依据；视觉消息流没有识别到有效消息时，本轮继续等待。
- 检测到新的待回复客户消息组后，等待 `MessageMergeWindowSeconds`，期间如果客户继续发送消息，会重新计算消息组并重新等待合并窗口。
- 每轮把待回复客户消息组作为 `CustomerQuestion`，把视觉消息列表格式化为 `ConversationContext`。
- 默认 `ReplyGroupingMode=Combined`，一组客户连续问题生成一条综合回复。
- 发送成功后记录消息组指纹和最近已回复消息文本，在 `DuplicateMessageSuppressMinutes` 内抑制重复回复。
- 每一轮真实回复都会创建新的 `RpaTask`，但使用同一个 `ConversationKey`，格式为 `single-continuous-{yyyyMMddHHmmss}`。
- 轮询阶段只写本地 UI 日志，不为“无新消息”创建后端空任务。

停止规则：

- 达到 `MaxContinuousSessionMinutes`。
- 达到 `MaxRepliesPerContinuousSession`。
- 连续失败达到 `MaxConsecutiveContinuousFailures`。
- OCR 低置信度、微信窗口/布局异常、AI 转人工、高风险、输入校验失败或发送失败。
- 员工点击暂停或紧急停止。
- 心跳返回授权不可继续运行。

M4.5 不降低 M4 的发送门槛：仍必须 `ShouldAutoSend=true`、`RiskLevel=Low`、输入框 OCR 校验通过、发送前倒计时结束且发送后输入框清空校验通过。

## 5. M4.5.3 OCR + VLM 视觉复核

M4.5.3 新增 `VisionOcrReviewer`。RPA 客户端在识别单条消息气泡时，先使用 Windows OCR / PaddleOCR；当 OCR 低置信、为空、短文本异常、数字混入中文或发送方未知时，调用本地或局域网 Ollama 视觉模型复核该气泡截图。

默认模型为 `qwen2.5vl:7b`。当前 `qwen2.5:7b` 是文本模型，不能直接识别截图；如需启用视觉复核，需要先执行：

```powershell
ollama pull qwen2.5vl:7b
```

VLM 只用于复核消息文字和发送方，不生成客服回复、不绕过后端 AI 风控。VLM 失败时默认跳过当前可疑消息并继续监听，避免因为 OCR 低置信度直接停止连续监听或增加人工处理投入。

## 5.1 M4.5.4 多屏窗口锁定

当前 RPA 的监听目标由微信窗口决定，不是由固定屏幕编号决定。系统通过窗口标题找到微信窗口后，把微信客户区转换成屏幕绝对坐标；如果微信窗口在第二块屏幕上，截图和点击也会落在第二块屏幕的对应坐标。

M4.5.4 已补强多屏和多微信窗口场景：

- 启动单次任务或连续监听时锁定本次微信窗口句柄、标题、客户区坐标、显示器边界和 DPI。
- UI 显示锁定窗口标题、句柄、客户区坐标、显示器边界和 DPI。
- 日志记录每轮实际监听目标和客户区坐标，便于排查“监听的是哪个屏幕”。
- 连续监听轮询时优先复用锁定窗口，不再单纯重新选择标题匹配且面积最大的窗口。
- 如果锁定窗口不可见、句柄失效、标题变化、显示器变化、DPI 变化或客户区尺寸超出容忍阈值，则停止本轮并提示员工重新选择目标窗口。
- 单次闭环在输入前和发送前再次校验锁定窗口，避免窗口被切走后继续点击输入框或发送按钮。

## 5.2 M4.5.5 性能诊断与加速

M4.5.5 已加入连续监听性能诊断和轻量化识别策略：

- 日志输出 `[性能]` 前缀，记录窗口定位、布局检测、聊天区截图、气泡候选检测、每条气泡 OCR、每次 VLM 复核、AI 回复接口和调试截图保存耗时。
- 连续监听轮询默认只 OCR 聊天区底部最近 8 个候选气泡，减少历史消息重复 OCR。
- VLM 默认只复核最新待回复客户消息组，当前真实测试使用 `AlwaysForCustomerMessages`，避免 OCR 高置信错字直接进入 AI，同时不复核整屏历史气泡。
- 微信窗口客户区尺寸不变时，连续监听复用上一次可用布局，跳过 `WeChatLayoutDetector`。
- 连续监听已识别出待回复消息组后，会把本轮窗口、布局和视觉消息流传给单次回复闭环，单次闭环不再重新执行整套视觉识别。

## 6. 停止与不发送规则

以下情况一律不点击发送按钮：

- 客户端未注册。
- 员工授权过期、暂停或禁用。
- 找不到微信窗口。
- 自动定位失败且配置兜底坐标无效。
- 截图区域无效。
- OCR 无文本。
- OCR 平均置信度低于阈值。
- 视觉消息流中最新有效消息发送方为未知。
- AI 调用失败或后端返回异常。
- AI 回复为空。
- AI 返回 `ShouldAutoSend=false`。
- 风险等级不是 `Low`。
- 输入框 OCR 校验失败。
- 发送后 OCR 校验发现输入框仍保留回复内容。
- 员工在输入、倒计时或发送前点击暂停。
- 员工点击紧急停止。

## 7. 日志与审计

每个关键步骤都会写入 `RpaActionLog`：

- 任务创建。
- 微信窗口定位。
- 微信布局自动定位和坐标来源。
- 客户消息 OCR。
- AI 回复建议。
- 点击输入框。
- 键盘输入完成。
- 输入框 OCR 校验。
- 发送按钮点击。
- 发送后输入框清空校验。
- 倒计时取消。
- 发送成功或跳过。
- 异常失败。

默认不保存完整截图。当前 M4 代码只回传 OCR 文本、AI 回复、风险结果和动作日志字段；调试阶段可通过 `EnableDebugCaptures` 保存本机 OCR 裁剪图，异常脱敏截图路径继续预留。

## 8. 验证结果

本轮 M4 视觉自动定位升级已完成代码级验证：

```powershell
dotnet build .\src\AIChat.RpaClient\AIChat.RpaClient.csproj -p:IntermediateOutputPath=obj\buildcheck\ -p:OutputPath=bin\buildcheck\
dotnet test .\tests\AIChat.UnitTests\AIChat.UnitTests.csproj
```

测试覆盖：

- RPA 客户端自动定位升级后可编译。
- 任务结果回写服务会更新非空字段。
- 空字段不会覆盖已有任务结果。
- 现有知识库、风控、结构化回复解析、授权判断测试继续通过。

真实微信发送验收进行中。验收时建议使用测试微信号和测试客户会话，并由员工在 VM 前值守，确认坐标、OCR、输入框和发送按钮位置无误。

## 9. 当前 M4.2

M4 当前仍以 OpenCV 布局规则为主。为了提升不同分辨率、不同窗口尺寸下的区域识别稳定性，已接入 M4.2 YOLO / ONNX 视觉识别旁路验证骨架。

M4.2 目标：

- 采集测试微信截图并标注关键区域。
- 训练 YOLO 模型并导出 ONNX。
- RPA 客户端本地加载 ONNX 模型做旁路推理；当前已支持 `Microsoft.ML.OnnxRuntime 1.28.0`。
- YOLO 结果和当前 OpenCV 结果同时输出到本机调试截图。
- 只评估识别质量，不直接使用 YOLO 坐标执行真实点击或发送。

M4.3 新增主动学习样本沉淀能力。开启 `EnableLearningSampleCapture=true` 后，RPA 每轮任务会在本机保存微信客户区截图、OpenCV 布局结果、YOLO 旁路检测结果、任务状态和草稿 YOLO 标签：

```text
%LOCALAPPDATA%\AIChat\RpaClient\learning-samples\accepted
%LOCALAPPDATA%\AIChat\RpaClient\learning-samples\review
```

成功且布局置信度达到 `LearningSampleMinReviewConfidence` 的样本进入 `accepted`；失败、低置信度、YOLO 异常、输入校验失败等样本进入 `review`。默认 `IncludeLearningSampleText=false`，metadata 不保存 OCR 原文和 AI 回复文本。该能力只用于离线训练数据沉淀，不在 RPA 进程内训练模型，不自动替换 ONNX，也不改变真实发送流程。

导入 RPA 样本到训练数据集：

```powershell
cd E:\Code\AIChat\tools\AIChat.VisionTrainer
.\.venv\Scripts\python.exe -m aichat_vision ingest-rpa --source "%LOCALAPPDATA%\AIChat\RpaClient\learning-samples" --dataset E:/Code/AIChat/datasets/wechat-layout --bucket review
```

导入后的标签仍是草稿，训练前必须用 LabelImg 或 CVAT 人工抽查修正。

当学习样本池累计到足够数量后，可以使用 VisionTrainer 离线执行一键主动学习训练管道：

训练前先检查可用样本是否达到 1000 张：

```powershell
cd E:\Code\AIChat\tools\AIChat.VisionTrainer
.\.venv\Scripts\python.exe -m aichat_vision active-learn --source "$env:LOCALAPPDATA\AIChat\RpaClient\learning-samples" --dataset E:/Code/AIChat/datasets/wechat-layout --min-samples 1000 --review-count 50 --version sample-check --dry-run
```

输出中以 `可用=<数量>` 为准。`可用` 会排除重复图片、缺少同名 `.txt` 标签的图片和空标签图片。粗略查看截图数时可执行：

```powershell
$root = "$env:LOCALAPPDATA\AIChat\RpaClient\learning-samples"
(Get-ChildItem "$root\review","$root\accepted" -Filter *.png -File -ErrorAction SilentlyContinue).Count
```

粗略统计只数 PNG，不判断标签和重复图片；正式训练前仍以 `active-learn --dry-run` 的 `可用` 数量为准。

确认 `可用 >= 1000` 后再运行：

```powershell
cd E:\Code\AIChat\tools\AIChat.VisionTrainer
.\.venv\Scripts\python.exe -m aichat_vision active-learn --source "$env:LOCALAPPDATA\AIChat\RpaClient\learning-samples" --dataset E:/Code/AIChat/datasets/wechat-layout --min-samples 1000 --review-count 50 --model yolo11n.pt --epochs 80 --imgsz 960 --batch 8 --device 0 --version m4.3-active-v1
```

该命令只在独立 Python 工具进程内运行：导入样本、生成 50 张复核抽样包、校验、划分、训练、预测、导出候选 ONNX，并安装到：

```text
%LOCALAPPDATA%\AIChat\RpaClient\models\wechat-layout-candidates\<version>\
```

候选模型不会自动覆盖正式模型。人工查看复核包和预测图确认可用后，再执行：

```powershell
.\.venv\Scripts\python.exe -m aichat_vision promote --candidate m4.3-active-v1 --install-local
```

`promote` 只复制 `wechat-layout.onnx`、`labels.txt`、`model-version.json` 到正式目录，不复制训练权重和报告，不删除其它文件。RPA 客户端不训练模型、不自动替换 ONNX、不新增后端 API 或数据库，也不改变 `ShouldAutoSend=true`、`RiskLevel=Low`、发送前倒计时、输入框 OCR 校验和 `SendMode` 等真实发送准入逻辑。

M4.2 第一版识别标签：

- `conversation_list`
- `chat_content`
- `input_area`
- `input_box`
- `send_button`
- `customer_message_bubble`
- `self_message_bubble`

M4.2 独立训练工具已落地到：

```text
tools/AIChat.VisionTrainer/
```

该工具通过 `python -m aichat_vision` 提供 `init`、`capture`、`prelabel`、`autolabel`、`validate`、`split`、`train`、`predict`、`export`、`package`、`ingest-rpa`、`active-learn`、`promote` 命令。`capture` 支持批量自动截图，`prelabel` 可先生成大区域标注草稿，`autolabel` 可在已有大区域框内按微信浅灰/绿色气泡和发送按钮位置自动补 4/5/6 三类草稿标签，减少人工标注工作。完成训练并导出后，执行：

```powershell
python -m aichat_vision package --artifact E:/Code/AIChat/artifacts/wechat-layout --install-local
```

会把 `wechat-layout.onnx`、`labels.txt`、`model-version.json` 安装到 RPA 默认模型目录。安装模型文件不会改变 `SendMode`、点击坐标或自动发送流程；M4.2 仍只读取模型做旁路识别质量验证。

M4.3 主动学习样本稳定沉淀后，再评估切换为 YOLO 优先定位：

- YOLO 坐标优先。
- OpenCV 布局规则兜底。
- 配置坐标最后兜底。
- 低置信度、关键标签缺失或结果冲突时一律不发送。

当前已进入 M4.5 单会话连续自动回复：

- 只监听员工当前打开的一个微信会话。
- 定时截图聊天内容区，解析客户 / 我方 / 系统 / 未知视觉消息流。
- 从下往上判断最新有效消息，最新是客户才回复，最新是我方则等待。
- 对已处理消息做去重，避免重复回复同一句。
- 客户短时间连续发送多条消息时，合并为一次上下文再调用 AI。
- 每一轮继续复用 M4 的 OCR、AI 回复、风控、输入框校验、审核倒计时和发送后校验。
- 支持监听间隔、连续运行时长、每轮回复次数和最小发送间隔配置。
- 员工暂停、紧急停止、OCR 低置信度、AI 转人工、高风险或发送失败时，停止连续自动回复并等待人工处理。
- YOLO / ONNX 在 M4.5 仍保持旁路验证，不接管真实点击和发送坐标。

M4.5 稳定后，再进入 M5 多会话队列：

- 识别未读会话入口。
- 构建单线程待处理队列。
- 按顺序切换客户会话。
- 每个会话复用 M4 单会话闭环。
- 增加每轮处理数量、发送频率、暂停恢复和异常跳过策略。
