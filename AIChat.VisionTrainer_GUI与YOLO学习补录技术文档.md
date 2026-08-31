# AIChat VisionTrainer GUI 与 YOLO 学习补录技术文档

更新时间：2026-08-13

## 1. 文档目的

本文档说明 `tools/AIChat.VisionTrainer` 中新增的本机 GUI、内置标注编辑器、规则补录、YOLO 学习补录、主动学习训练与候选模型转正流程。

目标是把原来需要手动输入 PowerShell 命令的流程，收敛为一个本机 Windows GUI：

```text
样本查看 → 样本标注 → 自动补录 → 主动学习训练 → 候选验收 → 模型转正
```

## 2. 适用范围

适用项目：

```text
E:\Code\AIChat\tools\AIChat.VisionTrainer
```

适用平台：

```text
Windows 本机
```

适用数据：

```text
%LOCALAPPDATA%\AIChat\RpaClient\learning-samples
E:\Code\AIChat\datasets\wechat-layout
```

当前 GUI 不负责：

```text
RPA 客户端运行控制
数据库变更
后端 API 调用
正式模型自动转正
生产数据删除
```

## 3. 核心概念

### 3.1 学习样本目录

RPA 客户端自动沉淀样本到：

```text
%LOCALAPPDATA%\AIChat\RpaClient\learning-samples
```

主要分桶：

```text
accepted        系统认为流程稳定的样本，修好后继续留在 accepted
review          异常、低置信度或失败流程样本，默认不建议直接训练
fixed           从 review 人工修好的样本池，建议和 accepted 一起训练
rejected        废图、错图、污染样本隔离区
```

训练分桶建议：

```text
accepted         只用 accepted
fixed            只用 fixed
accepted-fixed   使用 accepted + fixed，正式训练推荐
review           只用 review，仅排查或人工清洗后使用
all              accepted + fixed + review，不建议正式训练
```

每个样本通常由三件套组成：

```text
xxx.png   原始截图
xxx.txt   YOLO 标签
xxx.json  RPA metadata
```

### 3.2 YOLO 标签类别

当前类别固定 7 类，顺序不能随意改变：

```text
0 conversation_list
1 chat_content
2 input_area
3 input_box
4 send_button
5 customer_message_bubble
6 self_message_bubble
```

含义：

| 类别 | 含义 | 标注规则 |
|---|---|---|
| `conversation_list` | 左侧会话列表区域 | 框住搜索框、联系人列表、会话条目区域；不要框到右侧聊天区。 |
| `chat_content` | 右侧聊天内容区 | 框住聊天记录区域；不要包含底部输入区。 |
| `input_area` | 底部输入整体区域 | 包含工具栏、输入框、发送按钮所在区域。 |
| `input_box` | 实际文字输入框 | 只框真正可以输入文字的白色文本区域。 |
| `send_button` | 发送按钮 | 框住右下角发送按钮。 |
| `customer_message_bubble` | 对方/客户消息气泡 | 通常为左侧浅灰气泡，只框气泡本体，不含头像、时间、大面积空白。 |
| `self_message_bubble` | 自己发送消息气泡 | 通常为右侧绿色气泡，只框气泡本体，不含头像、时间、大面积空白。 |

### 3.3 自动补录分类

当前有两种自动补录能力：

```text
规则补录
YOLO 学习补录
```

区别：

| 能力 | 原理 | 优点 | 风险 |
|---|---|---|---|
| 规则补录 | 在 `chat_content` / `input_area` 内按颜色和位置规则找气泡、发送按钮 | 快，不需要训练 | 容易受主题、缩放、颜色、背景影响 |
| YOLO 学习补录 | 用已确认样本临时训练辅助 YOLO，再预测目标样本并写回标签 | 能从正确样本学习边界和形态 | 慢，需要已标注样本质量较好 |

## 4. 代码结构

新增/相关文件：

```text
tools/AIChat.VisionTrainer/aichat_vision/gui.py
tools/AIChat.VisionTrainer/aichat_vision/autolabel.py
tools/AIChat.VisionTrainer/aichat_vision/yolo_autolabel.py
tools/AIChat.VisionTrainer/aichat_vision/active_learn.py
tools/AIChat.VisionTrainer/aichat_vision/rpa_ingest.py
tools/AIChat.VisionTrainer/aichat_vision/cli.py
tools/AIChat.VisionTrainer/启动AIChat视觉训练GUI.cmd
```

### 4.1 `gui.py`

职责：

```text
Tkinter 本机 GUI
状态总览
内置标注编辑器
规则补录按钮
YOLO 学习补录按钮
主动学习训练控制
候选模型管理
设置保存
```

关键类：

```text
VisionTrainerApp
StatusTab
AnnotationEditorTab
TrainingTab
ModelTab
SettingsTab
AnnotationBox
GuiSettings
```

### 4.2 `autolabel.py`

职责：

```text
规则补录 send_button / customer_message_bubble / self_message_bubble
```

主要入口：

```python
autolabel_dataset(...)
autolabel_samples(...)
```

规则：

```text
在 chat_content 内找浅灰色左侧气泡 → customer_message_bubble
在 chat_content 内找绿色右侧气泡 → self_message_bubble
在 input_area 右下找发送按钮 → send_button
```

### 4.3 `yolo_autolabel.py`

职责：

```text
YOLO 学习补录
```

核心入口：

```python
yolo_autolabel_samples(...)
```

处理流程：

```text
1. 读取 train_bucket 中已标注样本。
2. 复制为临时 YOLO 数据集。
3. 调用 Ultralytics YOLO 训练辅助模型。
4. 用辅助模型预测 target_bucket 图片。
5. 将预测出的 4/5/6 三类框写回目标图片同名 .txt。
6. 写入 yolo-autolabel-report.json。
```

默认只追加不重叠预测框，不覆盖已有框。显式传入 `--overwrite-auto` 才覆盖：

```text
send_button
customer_message_bubble
self_message_bubble
```

### 4.4 `active_learn.py`

职责：

```text
主动学习一键训练候选模型
```

当前默认行为：

```text
默认 bucket = accepted
训练前执行规则补录
导入 RPA 样本
同步重复图片的标签
生成复核包
校验数据集
划分 train / val / test
训练 YOLO
预测测试集
导出 ONNX
安装候选模型
写 active-learn-report.json
```

注意：`active-learn` 当前默认接入的是**规则补录**，不是 YOLO 学习补录。YOLO 学习补录更慢，设计为 GUI/CLI 中单独触发。

### 4.5 `rpa_ingest.py`

职责：

```text
将 learning-samples 导入 dataset/raw
```

已优化点：

```text
如果图片 hash 已存在，不重复复制图片；但会同步更新 raw 中同 hash 图片的 .txt 标签和 metadata。
```

## 5. GUI 启动方式

### 5.1 双击启动

```text
tools/AIChat.VisionTrainer/启动AIChat视觉训练GUI.cmd
```

### 5.2 命令启动

```powershell
cd E:\Code\AIChat\tools\AIChat.VisionTrainer
.\.venv\Scripts\python.exe -m aichat_vision gui
```

### 5.3 自检启动

```powershell
.\.venv\Scripts\python.exe -m aichat_vision.gui --smoke-test
```

期望输出：

```text
GUI 自检通过：source=<learning-samples 路径>
```

## 6. GUI 页面说明

## 6.1 状态总览

用途：快速查看样本、数据集和模型状态。

显示内容：

```text
学习样本路径
数据集路径
候选模型路径
正式模型路径
accepted / fixed / review / rejected 图片数
各类别标签数量
各类别覆盖图片数量
accepted 可用样本数
数据集 train / val / test 数量
数据集校验错误和警告
候选模型列表
```

按钮：

| 按钮 | 功能 |
|---|---|
| 刷新状态 | 重新扫描样本、数据集、候选模型。 |
| 打开 accepted | 打开 accepted 样本目录。 |
| 打开 review | 打开 review 样本目录。 |
| 打开 rejected | 打开 rejected 隔离目录。 |
| 打开数据集 | 打开 `datasets/wechat-layout`。 |
| 打开候选模型 | 打开候选模型根目录。 |

## 6.2 样本标注

用途：替代 LabelImg，直接在 GUI 内查看和修改 YOLO 标签。

### 顶部操作

| 控件 | 功能 |
|---|---|
| 分桶 | 选择 `accepted` / `review` / `fixed`。 |
| 加载样本 | 加载当前分桶图片和 `.txt` 标签。 |
| 规则补录并覆盖 | 使用颜色规则重算 `send_button/customer/self` 三类框。 |
| YOLO学习补录 | 使用 `accepted` 训练辅助 YOLO，再补录当前分桶。 |
| 学习轮数 | YOLO 学习补录的临时训练 epochs。 |
| 置信度 | YOLO 学习补录的预测置信度阈值。 |
| 保存 | 保存当前图片标签。 |
| 上一张/下一张 | 切换样本。 |
| 移到 rejected | 将当前样本三件套移到 `rejected/gui-rejected`。 |
| 移到 fixed | 将当前样本三件套移到 `fixed`；推荐用于 review 中已经人工修好的样本。 |
| 适应窗口 | 图片适配画布。 |
| 放大/缩小 | 调整图片显示比例。 |

### 标注操作

| 操作 | 效果 |
|---|---|
| 选择当前类别后拖拽空白区域 | 新建标注框。 |
| 拖动框内部 | 移动框。 |
| 拖动框四角 | 缩放框。 |
| 点击框或列表项 | 选中框。 |
| 删除选中框 / Delete | 删除框。 |
| 选中框改为当前类别 | 修改类别。 |
| Ctrl+S | 保存当前 `.txt`。 |

### 框列表含义

格式：

```text
序号 类别名 宽x高
```

示例：

```text
06 customer_message_bubble 373x51
```

含义：第 6 个框，类别为对方消息气泡，框宽 373 像素、高 51 像素。

## 6.3 主动学习训练

用途：训练真正供 RPA 使用的候选 YOLO 模型。

路径参数：

| 参数 | 默认含义 |
|---|---|
| 学习样本目录 | RPA `learning-samples` 根目录。 |
| 数据集目录 | `datasets/wechat-layout`。 |
| 产物根目录 | `artifacts`。 |
| 候选模型根目录 | 本机 RPA 候选模型目录。 |

训练参数：

| 参数 | 含义 |
|---|---|
| 分桶 | 训练纳入 `accepted` / `fixed` / `accepted-fixed` / `review` / `all`。正式训练推荐 `accepted-fixed`。 |
| 最小样本 | 可用样本小于该值时停止训练。 |
| 复核数量 | 生成复核包时抽样数量。 |
| 基础模型 | YOLO 初始权重，例如 `yolo11n.pt`。 |
| epochs | 正式训练轮数。 |
| imgsz | 输入图片尺寸。 |
| batch | batch size。显存不足时调小。 |
| device | `0` 表示第一张 GPU，`cpu` 表示 CPU。 |
| version | 候选版本号。为空时自动生成。 |
| predict_conf | 训练后预测图使用的置信度。 |

按钮：

| 按钮 | 功能 |
|---|---|
| 只检查样本数量 | 执行 `active-learn --dry-run`，不导入、不训练。 |
| 安全自测 | 执行 `active-learn --skip-train`，补录、导入、校验、划分，但不训练。 |
| 开始训练候选模型 | 正式训练、预测、导出 ONNX、安装候选。执行前二次确认。 |
| 打开复核包 | 打开 `artifacts/wechat-layout-review/<version>`。 |
| 打开预测图 | 打开 `artifacts/wechat-layout-predict/<version>`。 |
| 打开候选产物 | 打开 `artifacts/wechat-layout-candidates/<version>`。 |

## 6.4 模型管理

用途：管理候选模型并手动转正。

按钮：

| 按钮 | 功能 |
|---|---|
| 刷新候选 | 重新扫描候选模型目录。 |
| 打开候选目录 | 打开选中的候选模型目录。 |
| 转正选中候选 | 将候选复制到正式模型目录。执行前二次确认。 |
| 打开正式模型目录 | 打开 RPA 当前正式模型目录。 |

转正只复制正式运行所需文件：

```text
wechat-layout.onnx
labels.txt
model-version.json
```

## 6.5 设置

配置文件：

```text
tools/AIChat.VisionTrainer/config/gui-settings.json
```

保存内容：

```text
学习样本目录
数据集目录
产物根目录
候选模型根目录
训练默认参数
YOLO 学习补录参数
自动补录默认选项
```

## 7. CLI 命令说明

### 7.1 规则补录样本

预览：

```powershell
python -m aichat_vision autolabel-samples --source "$env:LOCALAPPDATA\AIChat\RpaClient\learning-samples" --dry-run
```

写入并覆盖自动类：

```powershell
python -m aichat_vision autolabel-samples --source "$env:LOCALAPPDATA\AIChat\RpaClient\learning-samples" --overwrite
```

### 7.2 YOLO 学习补录

预检：

```powershell
python -m aichat_vision yolo-autolabel-samples `
  --source "$env:LOCALAPPDATA\AIChat\RpaClient\learning-samples" `
  --train-bucket accepted `
  --target-bucket accepted `
  --dry-run
```

执行：

```powershell
python -m aichat_vision yolo-autolabel-samples `
  --source "$env:LOCALAPPDATA\AIChat\RpaClient\learning-samples" `
  --train-bucket accepted `
  --target-bucket accepted `
  --epochs 30 `
  --imgsz 960 `
  --batch 8 `
  --device 0 `
  --conf 0.35
```

如确认辅助 YOLO 效果稳定，可显式覆盖旧自动类框：

```powershell
--overwrite-auto
```

### 7.3 主动学习检查

```powershell
python -m aichat_vision active-learn `
  --source "$env:LOCALAPPDATA\AIChat\RpaClient\learning-samples" `
  --dataset E:/Code/AIChat/datasets/wechat-layout `
  --min-samples 1000 `
  --review-count 50 `
  --version sample-check `
  --dry-run
```

### 7.4 主动学习安全自测

```powershell
python -m aichat_vision active-learn `
  --source "$env:LOCALAPPDATA\AIChat\RpaClient\learning-samples" `
  --dataset E:/Code/AIChat/datasets/wechat-layout `
  --min-samples 1000 `
  --review-count 50 `
  --version m4-active-check `
  --skip-train
```

### 7.5 正式训练候选模型

```powershell
python -m aichat_vision active-learn `
  --source "$env:LOCALAPPDATA\AIChat\RpaClient\learning-samples" `
  --dataset E:/Code/AIChat/datasets/wechat-layout `
  --min-samples 1000 `
  --review-count 50 `
  --model yolo11n.pt `
  --epochs 80 `
  --imgsz 960 `
  --batch 8 `
  --device 0 `
  --version m4-active-v1
```

### 7.6 候选模型转正

```powershell
python -m aichat_vision promote --candidate m4-active-v1 --install-local
```

## 8. 推荐日常流程

### 8.1 样本积累阶段

```text
1. RPA 正常运行，自动沉淀样本。
2. 打开 GUI。
3. 状态总览 → 刷新状态。
4. 样本标注 → accepted → 加载样本。
5. 手工修正几张典型图。
6. 点击 YOLO学习补录。
7. 抽查补录结果。
8. 坏图移到 rejected。
9. review 中修好的样本点“移到 fixed”。
10. 保存标签。
```

### 8.2 训练前检查阶段

```text
1. 主动学习训练 → 只检查样本数量。
2. 可用样本不足时继续积累。
3. 可用样本足够时执行安全自测。
4. 打开复核包，抽查标签质量。
```

### 8.3 正式训练与转正阶段

```text
1. 主动学习训练 → 开始训练候选模型。
2. 打开预测图，检查效果是否优于当前模型。
3. 模型管理 → 选择候选模型。
4. 转正选中候选。
```

## 9. 质量门槛建议

当前建议门槛：

```text
流程验证：20-50 张干净样本
小规模试训：300 张干净样本
相对稳定训练：1000 张干净样本
customer_message_bubble：至少 500 个
self_message_bubble：至少 500 个
```

正式训练前必须确认：

```text
accepted 中无明显废图
基础大框基本正确
消息气泡尽量补齐
没有大量越界标签
review 未经清洗不纳入训练；review 修好后移到 fixed，再用 accepted-fixed 训练
```

## 10. 风险与边界

### 10.1 自动补录不是最终真值

无论规则补录还是 YOLO 学习补录，输出都是草稿标签。训练前仍建议抽查和修正。

### 10.2 YOLO 学习补录依赖正确样本

如果 `accepted` 中人工确认样本本身标错，辅助 YOLO 会学习错误框，并把错误扩散到目标分桶。

### 10.3 不自动转正正式模型

任何训练或补录都不会自动替换正式 ONNX。正式模型转正必须人工点击 GUI 中的“转正选中候选”，或手动执行 `promote`。

### 10.4 review 默认不建议训练

`review` 是异常样本池，里面可能有黑图、半截图、遮罩图、低置信度样本。只有人工清洗后才建议纳入训练。

## 11. 验证记录

当前已验证：

```text
python -m compileall aichat_vision
python -m aichat_vision.gui --smoke-test
python -m aichat_vision gui --help
python -m aichat_vision yolo-autolabel-samples --help
python -m aichat_vision yolo-autolabel-samples --source C:/Users/Simon/AppData/Local/AIChat/RpaClient/learning-samples --train-bucket accepted --target-bucket accepted --dry-run --min-train-samples 5
```

已观察到：

```text
GUI 自检通过
YOLO 学习补录命令可用
当前 accepted 可作为 19 张辅助训练样本
```

未在本文档生成阶段执行正式 YOLO 学习补录训练，避免未经确认写回更多标签。
