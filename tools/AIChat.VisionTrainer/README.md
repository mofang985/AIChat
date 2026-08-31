# AIChat.VisionTrainer

`AIChat.VisionTrainer` 是 M4.2 独立 YOLO 视觉训练工具，只用于微信界面样本采集、YOLO 数据集整理、训练、预测、ONNX 导出和本机模型包安装。

它不控制微信、不点击、不输入、不发送消息，也不接入后端 API 或数据库。

## 环境安装

```powershell
cd E:\Code\AIChat\tools\AIChat.VisionTrainer
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

基础命令入口：

```powershell
python -m aichat_vision --help
```

启动本机 GUI：

```powershell
.\.venv\Scripts\python.exe -m aichat_vision gui
```

GUI 集成了状态总览、样本自动补录、内置标注编辑器、主动学习训练、候选模型转正和路径设置；正式训练和候选转正都会二次确认。


## 标签顺序

标签顺序固定，必须与 RPA 客户端 `YoloOnnxVisionDetector` 保持一致：

```text
0 conversation_list
1 chat_content
2 input_area
3 input_box
4 send_button
5 customer_message_bubble
6 self_message_bubble
```

## 1. 初始化数据集

```powershell
python -m aichat_vision init --dataset E:/Code/AIChat/datasets/wechat-layout
```

生成：

```text
datasets/wechat-layout/
  raw/
  images/train/
  images/val/
  images/test/
  labels/train/
  labels/val/
  labels/test/
  labels.txt
  data.yaml
```

## 2. 采集或导入截图

从当前微信窗口采集干净原始截图：

```powershell
python -m aichat_vision capture --source wechat --out E:/Code/AIChat/datasets/wechat-layout/raw
```

默认按窗口标题关键字 `微信` 定位可见窗口。该命令只截图，不会点击、输入或发送。

如果截图缺少底部输入区，先列出匹配到的微信窗口：

```powershell
python -m aichat_vision windows
```

多显示器或 4K 缩放环境下，再列出显示器坐标：

```powershell
python -m aichat_vision monitors
```

如果显示器是 3840x2160，但 `windows` 里微信窗口只有类似 2560x1392，通常是 Windows DPI 缩放导致的坐标虚拟化问题。工具会在截图前启用 Per-Monitor DPI awareness，确保后续窗口坐标和截图坐标尽量一致。

再改用完整窗口模式采集：

```powershell
python -m aichat_vision capture --source wechat --mode window --out E:/Code/AIChat/datasets/wechat-layout/raw --count 1
```

如果完整窗口模式仍不包含底部输入框，可以用屏幕模式兜底：

```powershell
python -m aichat_vision capture --source wechat --mode screen --out E:/Code/AIChat/datasets/wechat-layout/raw --count 1
```

确认单张截图包含左侧会话列表、右侧聊天区和底部输入区后，再批量采集。

批量自动截图：

```powershell
python -m aichat_vision capture --source wechat --mode window --out E:/Code/AIChat/datasets/wechat-layout/raw --count 100 --interval 2
```

这条命令会每 2 秒截一张，共 100 张。执行期间你只需要手动滚动聊天记录、切换测试会话、在输入框输入或清空测试文字，让画面覆盖更多状态。

从已有截图目录导入：

```powershell
python -m aichat_vision capture --source-dir D:/wechat-screenshots --out E:/Code/AIChat/datasets/wechat-layout/raw
```

若图片旁有同名 `.txt` 标签文件，会一起复制到 raw。

## 3. 半自动预标注

为了减少人工画框工作，可以先对 raw 截图生成第一版大区域预标注：

```powershell
python -m aichat_vision prelabel --dataset E:/Code/AIChat/datasets/wechat-layout
```

默认只预标注相对稳定的 4 个大区域：

- `conversation_list`
- `chat_content`
- `input_area`
- `input_box`

如果这批截图都确认发送按钮可见，可以额外生成固定位置 `send_button` 预标注：

```powershell
python -m aichat_vision prelabel --dataset E:/Code/AIChat/datasets/wechat-layout --include-send-button
```

默认不会覆盖已有 `.txt` 标签文件；确认要重新生成时再加 `--overwrite`。

预标注只是省时草稿，训练前必须人工复核框的位置，并补齐或修正：

- `send_button`
- `customer_message_bubble`
- `self_message_bubble`

如果截图中已经有较准确的 `chat_content` 和 `input_area` 大区域框，可以继续用颜色规则自动补标发送按钮和消息气泡。训练数据集 `raw` 使用：

```powershell
python -m aichat_vision autolabel --dataset E:/Code/AIChat/datasets/wechat-layout --dry-run
python -m aichat_vision autolabel --dataset E:/Code/AIChat/datasets/wechat-layout --overwrite
```

RPA 主动学习样本池使用 `autolabel-samples`，默认处理 `accepted`，不会读取 `rejected`：

```powershell
python -m aichat_vision autolabel-samples --source "$env:LOCALAPPDATA\AIChat\RpaClient\learning-samples" --dry-run
python -m aichat_vision autolabel-samples --source "$env:LOCALAPPDATA\AIChat\RpaClient\learning-samples" --overwrite
```

自动补标规则：

- 依赖已有 `chat_content` 和 `input_area` 大区域框，优先在区域内部搜索，降低误检。
- 在 `chat_content` 内按浅灰色气泡补 `customer_message_bubble`。
- 在 `chat_content` 内按绿色气泡补 `self_message_bubble`。
- 在 `input_area` 右下角识别绿色发送按钮；空输入框的浅灰按钮使用输入区右下相对位置兜底。
- 默认保留已有人工框，只追加不重叠的新框；如需重算 `send_button`、`customer_message_bubble`、`self_message_bubble` 三类，可追加 `--overwrite`。


如果已经有一批人工确认正确的样本，也可以用 `yolo-autolabel-samples` 临时训练一个辅助 YOLO 模型，再用它给目标分桶补录标签。这比颜色规则慢，但能从已确认图片中学习气泡大小、位置和边界；该辅助模型只用于补 `.txt`，不会替换正式 ONNX。

```powershell
python -m aichat_vision yolo-autolabel-samples --source "$env:LOCALAPPDATA\AIChat\RpaClient\learning-samples" --train-bucket accepted --target-bucket accepted --dry-run
python -m aichat_vision yolo-autolabel-samples --source "$env:LOCALAPPDATA\AIChat\RpaClient\learning-samples" --train-bucket accepted --target-bucket accepted --epochs 30 --imgsz 960 --batch 8 --device 0 --conf 0.35
```

建议流程：先人工修好一批 `accepted`，再用 YOLO 学习补录追加缺失框；确认效果稳定后，才可使用 `--overwrite-auto` 覆盖旧的 `send_button`、`customer_message_bubble`、`self_message_bubble` 三类框。

自动补标仍是训练草稿，建议用 LabelImg 抽查 10-20 张，重点删除误框、补漏框。后续主动学习训练默认会在导入前先对 `accepted` 样本执行同一套自动补标；如需跳过可加 `--no-autolabel-samples`。

如果你已经人工修好了一张图的大区域，可以把这张图的 `.txt` 当模板套到其它截图：

```powershell
python -m aichat_vision template --dataset E:/Code/AIChat/datasets/wechat-layout --from-label E:/Code/AIChat/datasets/wechat-layout/raw/wechat_xxx.txt --overwrite
```

模板命令默认只复制：

- `conversation_list`
- `chat_content`
- `input_area`
- `input_box`

如果同一批截图里发送按钮位置稳定且都可见，可以追加：

```powershell
python -m aichat_vision template --dataset E:/Code/AIChat/datasets/wechat-layout --from-label E:/Code/AIChat/datasets/wechat-layout/raw/wechat_xxx.txt --overwrite --include-send-button
```

消息气泡每张图差异大，建议先用 `autolabel` 生成草稿，再人工抽查修正 `customer_message_bubble` 和 `self_message_bubble`。

## 4. 导入 RPA 主动学习样本

RPA 客户端开启 `EnableLearningSampleCapture=true` 后，每轮任务会在本机保存学习样本：

%LOCALAPPDATA%\AIChat\RpaClient\learning-samples\accepted
%LOCALAPPDATA%\AIChat\RpaClient\learning-samples\fixed
%LOCALAPPDATA%\AIChat\RpaClient\learning-samples\review
%LOCALAPPDATA%\AIChat\RpaClient\learning-samples\rejected

导入人工确认后的样本：

```powershell
python -m aichat_vision ingest-rpa --source "%LOCALAPPDATA%\AIChat\RpaClient\learning-samples" --dataset E:/Code/AIChat/datasets/wechat-layout --bucket accepted-fixed
```

如果只想排查异常样本，可显式导入 `review`。`all` 会包含未处理的 `review`，不建议正式训练使用：

```powershell
python -m aichat_vision ingest-rpa --source "%LOCALAPPDATA%\AIChat\RpaClient\learning-samples" --dataset E:/Code/AIChat/datasets/wechat-layout --bucket review
```

`ingest-rpa` 会把 RPA 保存的 `.png` 和同名草稿 `.txt` 复制到 `dataset/raw`，按图片 hash 去重，并同步复制 `.json` metadata。草稿标签来自 OpenCV 布局和 YOLO 旁路结果，不等同于人工真值；训练前必须用 LabelImg 或 CVAT 抽查、修正。

## 5. 一键主动学习训练与候选升级

当 RPA 本机学习样本池累计到足够数量后，日常不需要再手动执行导入、校验、划分、训练、预测、导出和安装候选这一串命令，直接运行。`active-learn` 默认只纳入 `accepted`，并在导入前自动补标 `send_button`、`customer_message_bubble`、`self_message_bubble`，避免缺失气泡标签被模型当背景学习；`review` 修好后应移到 `fixed`，正式训练推荐 `--bucket accepted-fixed`，不要直接用未处理的 `review`。

先检查是否已经达到 1000 张可用样本：

```powershell
# 用途：精确检查 accepted 主动学习样本是否达到 1000 张；会先预览自动补标，再过滤重复图片、缺标签和空标签，不导入、不训练、不写模型。
.\.venv\Scripts\python.exe -m aichat_vision active-learn --source "$env:LOCALAPPDATA\AIChat\RpaClient\learning-samples" --dataset E:/Code/AIChat/datasets/wechat-layout --min-samples 1000 --review-count 50 --version sample-check --dry-run
```

输出中重点看 `可用=<数量>`：

```text
主动学习样本检查完成：扫描=1050, 可用=1008, 重复=20, 缺标签=10, 空标签=12
dry-run 模式：只完成样本检查，未导入、未训练、未写入产物。
```

如果样本不足，命令会停止并提示当前可用数量，例如：

```text
错误：主动学习样本不足，暂不训练：当前可用=750，要求=1000。
```

也可以粗略统计 `accepted` 中的 PNG 截图数量，但这个数字不判断同名 `.txt` 标签、空标签和重复图片，只能作为参考：

```powershell
# 用途：粗略统计本机 accepted 学习样本池中有多少张 png 截图。
$root = "$env:LOCALAPPDATA\AIChat\RpaClient\learning-samples"
(Get-ChildItem "$root\accepted" -Filter *.png -File -ErrorAction SilentlyContinue).Count
```

正式训练以 `active-learn --dry-run` 输出的 `可用` 数量为准。确认 `可用 >= 1000` 后，再运行：

```powershell
# 用途：样本达到 1000 张后，一键自动补标 accepted、去重、生成 50 张复核包、校验、划分、GPU 训练、预测、导出 ONNX，并安装到候选模型目录。
.\.venv\Scripts\python.exe -m aichat_vision active-learn --source "$env:LOCALAPPDATA\AIChat\RpaClient\learning-samples" --dataset E:/Code/AIChat/datasets/wechat-layout --min-samples 1000 --review-count 50 --model yolo11n.pt --epochs 80 --imgsz 960 --batch 8 --device 0 --version m4.3-active-v1
```

自动生成：

```text
E:\Code\AIChat\artifacts\wechat-layout-review\<version>\
E:\Code\AIChat\artifacts\wechat-layout-predict\<version>\
E:\Code\AIChat\artifacts\wechat-layout-candidates\<version>\
%LOCALAPPDATA%\AIChat\RpaClient\models\wechat-layout-candidates\<version>\
```

候选目录包含：

```text
wechat-layout.onnx
labels.txt
model-version.json
best.pt
active-learn-report.json
```

安全自测时可以跳过真实训练：

```powershell
# 用途：只验证样本数量、导入、复核包、校验和划分是否正常，不跑 GPU 训练，不导出模型。
.\.venv\Scripts\python.exe -m aichat_vision active-learn --source "$env:LOCALAPPDATA\AIChat\RpaClient\learning-samples" --dataset E:/Code/AIChat/datasets/wechat-layout --min-samples 1000 --review-count 50 --version m4.3-active-check --skip-train
```

人工抽查时只打开复核包目录或继续打开完整 raw 数据集：

```powershell
# 用途：抽查 active-learn 随机挑出的 50 张样本，重点看草稿框是否明显偏移、漏框或误框。
.\.venv\Scripts\labelImg.exe E:\Code\AIChat\artifacts\wechat-layout-review\m4.3-active-v1 E:\Code\AIChat\artifacts\wechat-layout-review\m4.3-active-v1\classes.txt
```

确认 `wechat-layout-predict\<version>` 的预测效果可用后，再显式转正：

```powershell
# 用途：把候选模型转正到 RPA 正式模型目录；只复制 onnx、labels.txt、model-version.json，不删除其它文件。
.\.venv\Scripts\python.exe -m aichat_vision promote --candidate m4.3-active-v1 --install-local
```

`active-learn` 只安装到 `wechat-layout-candidates`，不会覆盖正式 `wechat-layout`。正式替换必须执行 `promote`，避免错误候选模型自动进入当前 RPA 旁路验证环境。

## 6. GUI 内置标注与人工复核

第一版 GUI 已内置轻量标注编辑器，可直接打开 `learning-samples\accepted` / `review` / `fixed`。如需外部协作，仍可使用 CVAT 或 LabelImg，导出 YOLO detect 格式。

GUI 标注操作：

- 点击“规则补录并覆盖”可按微信浅灰/绿色气泡规则重算 `send_button`、`customer_message_bubble`、`self_message_bubble`。
- 点击“YOLO学习补录”会用 `accepted` 中已标注样本临时训练辅助 YOLO，再补录当前分桶；这是慢操作，会写入目标 `.txt` 标签，执行前会二次确认。
- 选择当前类别后，在图片空白处拖拽即可新建框。
- 拖动框内部可移动，拖动四角可缩放。
- 选中框后可删除，或改为当前类别。
- `Ctrl+S` 保存当前图片的 `.txt` 标签。
- “移到 rejected”会把当前样本的 `.png/.txt/.json` 三件套移到 `rejected/gui-rejected`。
- “移到 fixed”会把当前样本的 `.png/.txt/.json` 三件套移到 `fixed`，用于把修好的 review 样本纳入后续 `accepted-fixed` 训练。

标注要求：

- 每张图至少标注 `conversation_list`、`chat_content`、`input_area`、`input_box`；若已执行 `prelabel` 或 `active-learn` 草稿导入，需要人工抽查复核这些框。
- `send_button` 仅在按钮可见时标注。
- 客户左侧消息气泡标 `customer_message_bubble`。
- 自己右侧消息气泡标 `self_message_bubble`。
- 消息气泡框贴近气泡外边界，不包含头像和时间。

## 7. 校验与划分

```powershell
python -m aichat_vision validate --dataset E:/Code/AIChat/datasets/wechat-layout

python -m aichat_vision split --dataset E:/Code/AIChat/datasets/wechat-layout --val-ratio 0.2 --test-ratio 0.1

python -m aichat_vision validate --dataset E:/Code/AIChat/datasets/wechat-layout
```

`split` 使用固定随机种子，默认复制为 70% train、20% val、10% test。

## 8. 训练与预测

```powershell
python -m aichat_vision train --dataset E:/Code/AIChat/datasets/wechat-layout --model yolo26n.pt --epochs 100 --imgsz 640 --batch 8
```

当前 v2 GPU 验收模型使用：

```powershell
python -m aichat_vision train --dataset E:/Code/AIChat/datasets/wechat-layout --model yolo11n.pt --epochs 80 --imgsz 960 --batch 8 --device 0 --name m42-poc-v2
```

CPU 临时跑通：

```powershell
python -m aichat_vision train --dataset E:/Code/AIChat/datasets/wechat-layout --model yolo26n.pt --epochs 30 --imgsz 640 --batch 2 --device cpu
```

预测测试截图并输出可视化图：

```powershell
python -m aichat_vision predict --weights E:/Code/AIChat/tools/AIChat.VisionTrainer/runs/detect/train/weights/best.pt --source E:/Code/AIChat/datasets/wechat-layout/images/test --out E:/Code/AIChat/artifacts/wechat-layout/predict
```

v2 预测验收：

```powershell
python -m aichat_vision predict --weights E:/Code/AIChat/tools/AIChat.VisionTrainer/runs/detect/m42-poc-v2/weights/best.pt --source E:/Code/AIChat/datasets/wechat-layout/images/test --out E:/Code/AIChat/artifacts/wechat-layout/predict-v2 --imgsz 960 --conf 0.15
```

## 9. 导出 ONNX

```powershell
python -m aichat_vision export --weights E:/Code/AIChat/tools/AIChat.VisionTrainer/runs/detect/train/weights/best.pt --out E:/Code/AIChat/artifacts/wechat-layout --dataset E:/Code/AIChat/datasets/wechat-layout --imgsz 640
```

v2 导出：

```powershell
python -m aichat_vision export --weights E:/Code/AIChat/tools/AIChat.VisionTrainer/runs/detect/m42-poc-v2/weights/best.pt --out E:/Code/AIChat/artifacts/wechat-layout-v2 --dataset E:/Code/AIChat/datasets/wechat-layout --imgsz 960 --version m4.2-v2
```

生成：

```text
artifacts/wechat-layout/
  best.pt
  wechat-layout.onnx
  labels.txt
  model-version.json
```

## 10. 安装到 RPA 客户端

```powershell
python -m aichat_vision package --artifact E:/Code/AIChat/artifacts/wechat-layout --install-local
```

v2 安装：

```powershell
python -m aichat_vision package --artifact E:/Code/AIChat/artifacts/wechat-layout-v2 --install-local
```

复制到：

```text
%LOCALAPPDATA%\AIChat\RpaClient\models\wechat-layout\
```

RPA 客户端配置中 `YoloModelPath` 和 `YoloLabelsPath` 为空时，会默认读取该目录下的 `wechat-layout.onnx` 与 `labels.txt`。

当前 v2 模型使用 `imgsz=960` 导出，RPA 客户端应设置 `YoloInputSize=960`。当前默认联调配置为 `SendMode=InputOnly`、`InputOnlyAfterVerifyAction=ClearInput`、`EnableYoloLayoutValidation=true`，只输入并校验回复，校验后清空草稿，不点击发送；真实发送验收前必须显式切换发送模式并确认微信停在测试会话。

## 建议

第一轮可以先用 `capture --count 100 --interval 2` 自动采集 100 张 POC 样本，再用 `prelabel` 生成草稿标签并人工修正。内测模型建议扩展到 300-500 张，并覆盖至少 1920x1080、2560x1440 和一种非最大化窗口尺寸。
