from __future__ import annotations

import argparse
import sys
from pathlib import Path

from .active_learn import run_active_learning
from .autolabel import autolabel_dataset, autolabel_samples
from .capture import capture_wechat_windows, import_images_from_directory, list_monitors, list_wechat_windows
from .dataset import ensure_dataset_structure, split_dataset
from .errors import VisionTrainerError
from .export import export_onnx
from .package import package_artifact
from .prelabel import prelabel_dataset
from .predict import predict_images
from .promote import promote_candidate
from .rpa_ingest import ingest_rpa_samples
from .template import apply_template
from .train import train_model
from .validate import validate_dataset
from .yolo_autolabel import yolo_autolabel_samples


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        prog="python -m aichat_vision",
        description="AIChat M4.2 独立 YOLO 视觉训练工具。",
    )
    subparsers = parser.add_subparsers(dest="command")

    init_parser = subparsers.add_parser("init", help="创建默认 YOLO 数据集目录、labels.txt 和 data.yaml。")
    init_parser.add_argument("--dataset", required=True, help="数据集目录，例如 E:/Code/AIChat/datasets/wechat-layout。")
    init_parser.set_defaults(func=_cmd_init)

    capture_parser = subparsers.add_parser("capture", help="采集微信原始截图，或从已有目录导入截图。")
    capture_parser.add_argument("--source", choices=["wechat"], default="wechat", help="截图来源，默认 wechat。")
    capture_parser.add_argument("--source-dir", help="从已有图片目录导入。")
    capture_parser.add_argument("--out", required=True, help="输出 raw 目录。")
    capture_parser.add_argument("--title-keyword", default="微信", help="微信窗口标题关键字，默认“微信”。")
    capture_parser.add_argument("--count", type=int, default=1, help="连续截图数量，默认 1。")
    capture_parser.add_argument("--interval", type=float, default=2.0, help="连续截图间隔秒数，默认 2。")
    capture_parser.add_argument("--mode", choices=["client", "window", "screen"], default="client", help="截图模式：client=微信客户区，window=完整窗口，screen=完整可见桌面。")
    capture_parser.add_argument("--window-index", type=int, default=0, help="多个微信窗口匹配时选择窗口序号，先用 windows 命令查看。")
    capture_parser.set_defaults(func=_cmd_capture)

    windows_parser = subparsers.add_parser("windows", help="列出标题匹配的可见微信窗口，辅助排查截图对象。")
    windows_parser.add_argument("--title-keyword", default="微信", help="微信窗口标题关键字，默认“微信”。")
    windows_parser.set_defaults(func=_cmd_windows)

    monitors_parser = subparsers.add_parser("monitors", help="列出 Windows 显示器坐标，辅助排查多屏/DPI 截图问题。")
    monitors_parser.set_defaults(func=_cmd_monitors)

    prelabel_parser = subparsers.add_parser("prelabel", help="为 raw 截图生成第一版半自动 YOLO 预标注。")
    prelabel_parser.add_argument("--dataset", required=True, help="数据集目录。")
    prelabel_parser.add_argument("--overwrite", action="store_true", help="覆盖已有 .txt 标签文件。")
    prelabel_parser.add_argument("--include-send-button", action="store_true", help="额外预标注固定位置 send_button；仅建议用于发送按钮可见的截图。")
    prelabel_parser.set_defaults(func=_cmd_prelabel)

    autolabel_parser = subparsers.add_parser("autolabel", help="按微信颜色规则自动补标发送按钮和消息气泡。")
    autolabel_parser.add_argument("--dataset", required=True, help="数据集目录。")
    autolabel_parser.add_argument("--overwrite", action="store_true", help="覆盖已有 send_button、customer_message_bubble、self_message_bubble 标签。")
    autolabel_parser.add_argument("--dry-run", action="store_true", help="只统计将要补标的数量，不写入 .txt。")
    autolabel_parser.add_argument("--max-width", type=int, default=1280, help="检测时的最大缩放宽度，默认 1280。")
    autolabel_parser.set_defaults(func=_cmd_autolabel)

    autolabel_samples_parser = subparsers.add_parser("autolabel-samples", help="按微信颜色规则自动补标 RPA 学习样本目录。")
    autolabel_samples_parser.add_argument("--source", required=True, help="RPA learning-samples 根目录，或其中的 accepted/review/fixed 子目录。")
    autolabel_samples_parser.add_argument("--bucket", choices=["source", "accepted", "review", "fixed", "accepted-fixed", "all"], default="accepted", help="样本分桶；默认 accepted，source 表示直接处理 --source 目录。")
    autolabel_samples_parser.add_argument("--overwrite", action="store_true", help="覆盖已有 send_button、customer_message_bubble、self_message_bubble 标签。")
    autolabel_samples_parser.add_argument("--dry-run", action="store_true", help="只统计将要补标的数量，不写入 .txt。")
    autolabel_samples_parser.add_argument("--max-width", type=int, default=1280, help="检测时的最大缩放宽度，默认 1280。")
    autolabel_samples_parser.set_defaults(func=_cmd_autolabel_samples)

    yolo_autolabel_parser = subparsers.add_parser("yolo-autolabel-samples", help="用已标注样本临时训练 YOLO 辅助模型，再自动补录 RPA 学习样本。")
    yolo_autolabel_parser.add_argument("--source", required=True, help="RPA learning-samples 根目录，或其中的 accepted/review/fixed 子目录。")
    yolo_autolabel_parser.add_argument("--train-bucket", choices=["source", "accepted", "review", "fixed", "accepted-fixed", "all"], default="accepted", help="用于训练辅助 YOLO 的样本分桶，默认 accepted。")
    yolo_autolabel_parser.add_argument("--target-bucket", choices=["source", "accepted", "review", "fixed", "accepted-fixed", "all"], default="accepted", help="要自动补录的样本分桶，默认 accepted。")
    yolo_autolabel_parser.add_argument("--model", default="yolo11n.pt", help="基础模型权重，默认 yolo11n.pt。")
    yolo_autolabel_parser.add_argument("--epochs", type=int, default=30, help="辅助 YOLO 训练轮数，默认 30。")
    yolo_autolabel_parser.add_argument("--imgsz", type=int, default=960, help="训练和预测输入尺寸，默认 960。")
    yolo_autolabel_parser.add_argument("--batch", type=int, default=8, help="batch size，默认 8。")
    yolo_autolabel_parser.add_argument("--device", default="0", help="训练设备，默认 0；CPU 可填 cpu。")
    yolo_autolabel_parser.add_argument("--conf", type=float, default=0.35, help="补录预测置信度阈值，默认 0.35。")
    yolo_autolabel_parser.add_argument("--version", help="辅助补录版本号，不填自动生成。")
    yolo_autolabel_parser.add_argument("--artifacts-root", help="产物根目录，默认 E:/Code/AIChat/artifacts。")
    yolo_autolabel_parser.add_argument("--overwrite-auto", action="store_true", help="按 YOLO 预测结果覆盖 send_button/customer/self 三类旧框。")
    yolo_autolabel_parser.add_argument("--dry-run", action="store_true", help="只检查训练样本和目标样本数量，不训练、不写入。")
    yolo_autolabel_parser.add_argument("--min-train-samples", type=int, default=5, help="辅助训练最少样本数，默认 5。")
    yolo_autolabel_parser.set_defaults(func=_cmd_yolo_autolabel_samples)

    template_parser = subparsers.add_parser("template", help="把一张已修好的标签作为模板复制到 raw 其它图片。")
    template_parser.add_argument("--dataset", required=True, help="数据集目录。")
    template_parser.add_argument("--from-label", required=True, help="已人工修好的模板 .txt 标签文件。")
    template_parser.add_argument("--overwrite", action="store_true", help="覆盖其它图片中已有的大区域标签。")
    template_parser.add_argument("--include-send-button", action="store_true", help="同时复制 send_button；仅当发送按钮位置和可见状态稳定时使用。")
    template_parser.set_defaults(func=_cmd_template)

    validate_parser = subparsers.add_parser("validate", help="校验 YOLO 图片和标签文件。")
    validate_parser.add_argument("--dataset", required=True, help="数据集目录。")
    validate_parser.set_defaults(func=_cmd_validate)

    split_parser = subparsers.add_parser("split", help="按固定随机种子划分 train / val / test。")
    split_parser.add_argument("--dataset", required=True, help="数据集目录。")
    split_parser.add_argument("--val-ratio", type=float, default=0.2, help="验证集比例，默认 0.2。")
    split_parser.add_argument("--test-ratio", type=float, default=0.1, help="测试集比例，默认 0.1。")
    split_parser.add_argument("--seed", type=int, default=42, help="随机种子，默认 42。")
    split_parser.set_defaults(func=_cmd_split)

    train_parser = subparsers.add_parser("train", help="调用 Ultralytics YOLO 训练模型。")
    train_parser.add_argument("--dataset", required=True, help="数据集目录。")
    train_parser.add_argument("--model", default="yolo26n.pt", help="基础模型权重，默认 yolo26n.pt。")
    train_parser.add_argument("--epochs", type=int, default=100, help="训练轮数。")
    train_parser.add_argument("--imgsz", type=int, default=640, help="输入尺寸。")
    train_parser.add_argument("--batch", type=int, default=8, help="batch size。")
    train_parser.add_argument("--device", help="训练设备，例如 cpu、0。")
    train_parser.add_argument("--name", default="train", help="Ultralytics 运行名称，默认 train。")
    train_parser.set_defaults(func=_cmd_train)

    predict_parser = subparsers.add_parser("predict", help="使用训练权重预测图片并保存可视化结果。")
    predict_parser.add_argument("--weights", required=True, help="best.pt 路径。")
    predict_parser.add_argument("--source", required=True, help="图片或图片目录。")
    predict_parser.add_argument("--out", required=True, help="预测可视化输出目录。")
    predict_parser.add_argument("--imgsz", type=int, help="预测输入尺寸，默认由模型决定。")
    predict_parser.add_argument("--conf", type=float, default=0.25, help="预测置信度阈值，默认 0.25。")
    predict_parser.set_defaults(func=_cmd_predict)

    export_parser = subparsers.add_parser("export", help="导出 ONNX 并生成 RPA 可安装模型包。")
    export_parser.add_argument("--weights", required=True, help="best.pt 路径。")
    export_parser.add_argument("--out", required=True, help="模型产物目录。")
    export_parser.add_argument("--imgsz", type=int, default=640, help="ONNX 输入尺寸。")
    export_parser.add_argument("--dataset", help="可选数据集目录，用于写入 model-version.json 数据量。")
    export_parser.add_argument("--version", default="m4.2-001", help="模型版本号。")
    export_parser.add_argument("--opset", type=int, help="可选 ONNX opset。")
    export_parser.add_argument("--simplify", action="store_true", help="启用 ONNX simplify。")
    export_parser.set_defaults(func=_cmd_export)

    package_parser = subparsers.add_parser("package", help="校验模型包并可安装到 RPA 本机模型目录。")
    package_parser.add_argument("--artifact", required=True, help="模型产物目录。")
    package_parser.add_argument("--install-local", action="store_true", help="复制到 %%LOCALAPPDATA%%/AIChat/RpaClient/models/wechat-layout。")
    package_parser.add_argument("--dest", help="可选自定义安装目录，优先于 --install-local。")
    package_parser.set_defaults(func=_cmd_package)

    ingest_parser = subparsers.add_parser("ingest-rpa", help="导入 RPA 主动学习样本到 raw 数据集。")
    ingest_parser.add_argument("--source", required=True, help="RPA learning-samples 目录，或其中的 accepted/review/fixed 子目录。")
    ingest_parser.add_argument("--dataset", required=True, help="目标 YOLO 数据集目录。")
    ingest_parser.add_argument("--bucket", choices=["review", "accepted", "fixed", "accepted-fixed", "all"], default="accepted", help="导入样本分桶，默认 accepted；正式训练推荐 accepted-fixed。")
    ingest_parser.set_defaults(func=_cmd_ingest_rpa)

    active_parser = subparsers.add_parser("active-learn", help="一键执行 RPA 主动学习导入、复核包、训练、预测、导出和候选安装。")
    active_parser.add_argument("--source", required=True, help="RPA learning-samples 目录，通常为 %%LOCALAPPDATA%%/AIChat/RpaClient/learning-samples。")
    active_parser.add_argument("--dataset", required=True, help="目标 YOLO 数据集目录。")
    active_parser.add_argument("--min-samples", type=int, default=1000, help="开始训练前要求的可用学习样本数，默认 1000。")
    active_parser.add_argument("--review-count", type=int, default=50, help="生成复核抽样包的图片数，默认 50。")
    active_parser.add_argument("--bucket", choices=["review", "accepted", "fixed", "accepted-fixed", "all"], default="accepted", help="纳入的样本分桶，默认 accepted；正式训练推荐 accepted-fixed，review/all 仅建议人工清洗后显式使用。")
    active_parser.add_argument("--model", default="yolo11n.pt", help="基础模型权重，默认 yolo11n.pt。")
    active_parser.add_argument("--epochs", type=int, default=80, help="训练轮数，默认 80。")
    active_parser.add_argument("--imgsz", type=int, default=960, help="训练、预测和 ONNX 导出输入尺寸，默认 960。")
    active_parser.add_argument("--batch", type=int, default=8, help="batch size，默认 8。")
    active_parser.add_argument("--device", default="0", help="训练设备，默认 0；CPU 可填 cpu。")
    active_parser.add_argument("--version", help="候选模型版本号，不填时自动生成 m4.3-active-时间戳。")
    active_parser.add_argument("--val-ratio", type=float, default=0.2, help="验证集比例，默认 0.2。")
    active_parser.add_argument("--test-ratio", type=float, default=0.1, help="测试集比例，默认 0.1。")
    active_parser.add_argument("--seed", type=int, default=42, help="抽样和划分随机种子，默认 42。")
    active_parser.add_argument("--predict-conf", type=float, default=0.15, help="预测可视化置信度阈值，默认 0.15。")
    active_parser.add_argument("--artifacts-root", help="产物根目录，默认 E:/Code/AIChat/artifacts。")
    active_parser.add_argument("--candidate-root", help="候选模型安装根目录，默认 %%LOCALAPPDATA%%/AIChat/RpaClient/models/wechat-layout-candidates。")
    active_parser.add_argument("--skip-train", action="store_true", help="只执行导入、复核包、校验和划分，不训练、不预测、不导出、不安装候选。")
    active_parser.add_argument("--dry-run", action="store_true", help="只检查样本数量和可用性，不写入数据集、不训练。")
    active_parser.add_argument("--no-install-candidate", action="store_true", help="导出候选产物后不复制到 RPA 本机候选模型目录。")
    active_parser.add_argument("--opset", type=int, help="可选 ONNX opset。")
    active_parser.add_argument("--simplify", action="store_true", help="启用 ONNX simplify。")
    active_parser.add_argument("--no-autolabel-samples", action="store_true", help="跳过训练前 RPA 样本颜色规则自动补标。")
    active_parser.add_argument("--autolabel-overwrite", action="store_true", help="训练前重算 send_button、customer_message_bubble、self_message_bubble 标签。")
    active_parser.add_argument("--autolabel-max-width", type=int, default=1280, help="训练前自动补标检测缩放宽度，默认 1280。")
    active_parser.set_defaults(func=_cmd_active_learn)

    promote_parser = subparsers.add_parser("promote", help="人工确认候选效果后，把候选模型转正到 RPA 正式模型目录。")
    promote_parser.add_argument("--candidate", required=True, help="候选版本号，例如 m4.3-active-v1；也可以传候选目录路径。")
    promote_parser.add_argument("--install-local", action="store_true", help="复制到 %%LOCALAPPDATA%%/AIChat/RpaClient/models/wechat-layout。")
    promote_parser.add_argument("--candidate-root", help="候选模型根目录，默认 %%LOCALAPPDATA%%/AIChat/RpaClient/models/wechat-layout-candidates。")
    promote_parser.add_argument("--dest", help="可选自定义正式模型目录，优先于 --install-local。")
    promote_parser.set_defaults(func=_cmd_promote)

    gui_parser = subparsers.add_parser("gui", help="启动本机 Tkinter 图形界面。")
    gui_parser.set_defaults(func=_cmd_gui)

    return parser


def main(argv: list[str] | None = None) -> int:
    parser = build_parser()
    args = parser.parse_args(argv)
    if not hasattr(args, "func"):
        parser.print_help()
        return 0

    try:
        return int(args.func(args) or 0)
    except VisionTrainerError as exc:
        print(f"错误：{exc}", file=sys.stderr)
        return 2


def _cmd_init(args: argparse.Namespace) -> int:
    dataset = Path(args.dataset)
    ensure_dataset_structure(dataset)
    print(f"已初始化数据集：{dataset.resolve()}")
    return 0


def _cmd_ingest_rpa(args: argparse.Namespace) -> int:
    summary = ingest_rpa_samples(
        source=Path(args.source),
        dataset=Path(args.dataset),
        bucket=args.bucket,
    )
    print(
        "RPA 主动学习样本导入完成："
        f"扫描={summary.scanned}, 导入={summary.imported}, "
        f"重复跳过={summary.skipped_duplicates}, 同步重复标签={summary.updated_duplicate_labels}, "
        f"缺标签跳过={summary.skipped_missing_labels}, 空标签跳过={summary.skipped_empty_labels}"
    )
    print("导入的是 RPA 草稿标签，训练前仍建议用 LabelImg 或 CVAT 人工抽查和修正。")
    return 0


def _cmd_active_learn(args: argparse.Namespace) -> int:
    summary = run_active_learning(
        source=Path(args.source),
        dataset=Path(args.dataset),
        min_samples=args.min_samples,
        review_count=args.review_count,
        bucket=args.bucket,
        model=args.model,
        epochs=args.epochs,
        imgsz=args.imgsz,
        batch=args.batch,
        device=args.device,
        version=args.version,
        val_ratio=args.val_ratio,
        test_ratio=args.test_ratio,
        seed=args.seed,
        predict_conf=args.predict_conf,
        artifacts_root=Path(args.artifacts_root) if args.artifacts_root else None,
        candidate_root=Path(args.candidate_root) if args.candidate_root else None,
        install_candidate=not args.no_install_candidate,
        skip_train=args.skip_train,
        dry_run=args.dry_run,
        opset=args.opset,
        simplify=args.simplify,
        autolabel_samples_enabled=not args.no_autolabel_samples,
        autolabel_overwrite=args.autolabel_overwrite,
        autolabel_max_width=args.autolabel_max_width,
    )

    inventory = summary.inventory
    print(
        "主动学习样本检查完成："
        f"扫描={inventory.scanned_images}, 可用={inventory.usable_samples}, "
        f"重复={inventory.skipped_duplicates}, 缺标签={inventory.skipped_missing_labels}, "
        f"空标签={inventory.skipped_empty_labels}"
    )
    _print_autolabel_summary("训练前 RPA 样本规则自动补标", summary.autolabel_summary, dry_run=summary.dry_run)

    if summary.dry_run:
        print("dry-run 模式：只完成样本检查，未导入、未训练、未写入产物。")
        return 0

    if summary.ingest:
        print(
            "RPA 样本导入完成："
            f"扫描={summary.ingest.scanned}, 导入={summary.ingest.imported}, "
            f"重复跳过={summary.ingest.skipped_duplicates}, 同步重复标签={summary.ingest.updated_duplicate_labels}"
        )
    if summary.review_dir:
        print(f"复核抽样包：{summary.review_dir}")
    if summary.split:
        print(f"数据集划分：train={summary.split.train}, val={summary.split.val}, test={summary.split.test}")

    if summary.skip_train:
        print(f"已按 --skip-train 停止；流程报告：{summary.report_path}")
        return 0

    print(f"训练输出目录：{summary.train_dir}")
    print(f"预测验收目录：{summary.predict_dir}" if summary.predict_dir else "预测验收目录：测试集为空，已跳过预测。")
    print(f"候选产物目录：{summary.artifact_dir}")
    if summary.candidate_install_dir:
        print(f"候选模型已安装到：{summary.candidate_install_dir}")
    else:
        print("候选模型未安装到本机候选目录。")
    print(f"流程报告：{summary.report_path}")
    print("注意：正式模型目录未被覆盖；确认预测效果后再执行 promote。")
    return 0


def _cmd_promote(args: argparse.Namespace) -> int:
    summary = promote_candidate(
        candidate=args.candidate,
        install_local=args.install_local,
        candidate_root=Path(args.candidate_root) if args.candidate_root else None,
        destination=Path(args.dest) if args.dest else None,
    )
    if summary.installed_dir:
        print(
            "候选模型已转正："
            f"{summary.candidate_dir} -> {summary.installed_dir}，"
            f"复制文件={', '.join(summary.copied_files)}"
        )
    else:
        print(f"候选模型文件完整：{summary.candidate_dir}；未指定 --install-local 或 --dest，因此未复制。")
    return 0


def _cmd_capture(args: argparse.Namespace) -> int:
    out = Path(args.out)
    if args.source_dir:
        count = import_images_from_directory(Path(args.source_dir), out)
        print(f"已导入 {count} 张图片到：{out.resolve()}")
        return 0

    targets = capture_wechat_windows(out, args.title_keyword, args.count, args.interval, args.mode, args.window_index)
    if len(targets) == 1:
        print(f"已采集微信截图：{targets[0]}")
    else:
        print(f"已采集 {len(targets)} 张微信截图，输出目录：{out.resolve()}")
        print(f"首张：{targets[0]}")
        print(f"末张：{targets[-1]}")
    return 0


def _cmd_windows(args: argparse.Namespace) -> int:
    windows = list_wechat_windows(args.title_keyword)
    if not windows:
        print(f"未找到标题包含“{args.title_keyword}”的可见窗口。")
        return 1

    for window in windows:
        client_width = window.client_rect[2] - window.client_rect[0]
        client_height = window.client_rect[3] - window.client_rect[1]
        window_width = window.window_rect[2] - window.window_rect[0]
        window_height = window.window_rect[3] - window.window_rect[1]
        print(
            f"[{window.index}] hwnd={window.hwnd} title={window.title!r} "
            f"client={window.client_rect} {client_width}x{client_height} "
            f"window={window.window_rect} {window_width}x{window_height}"
        )

    return 0


def _cmd_monitors(args: argparse.Namespace) -> int:
    monitors = list_monitors()
    if not monitors:
        print("未找到显示器。")
        return 1

    for monitor in monitors:
        width = monitor.monitor_rect[2] - monitor.monitor_rect[0]
        height = monitor.monitor_rect[3] - monitor.monitor_rect[1]
        primary = " primary" if monitor.primary else ""
        print(
            f"[{monitor.index}]{primary} device={monitor.device!r} "
            f"monitor={monitor.monitor_rect} {width}x{height} "
            f"work={monitor.work_rect}"
        )

    return 0


def _cmd_prelabel(args: argparse.Namespace) -> int:
    summary = prelabel_dataset(Path(args.dataset), args.overwrite, args.include_send_button)
    print(
        "预标注完成："
        f"新建={summary.created}, 跳过已有={summary.skipped}, 覆盖={summary.overwritten}"
    )
    print("请用 CVAT 或 LabelImg 复核并补充 send_button、customer_message_bubble、self_message_bubble。")
    return 0


def _cmd_autolabel(args: argparse.Namespace) -> int:
    summary = autolabel_dataset(
        dataset=Path(args.dataset),
        overwrite=args.overwrite,
        dry_run=args.dry_run,
        max_width=args.max_width,
    )
    mode_text = "预览" if args.dry_run else "写入"
    print(
        f"规则自动补标{mode_text}完成："
        f"图片={summary.checked_images}, 更新={summary.updated_images}, 未变={summary.unchanged_images}, "
        f"新建标签={summary.created_labels}, send_button={summary.added_send_buttons}, "
        f"customer_message_bubble={summary.added_customer_bubbles}, "
        f"self_message_bubble={summary.added_self_bubbles}"
    )
    print("该命令按颜色和区域规则生成草稿标签，训练前仍建议用 LabelImg 抽查并修正误框。")
    return 0


def _cmd_autolabel_samples(args: argparse.Namespace) -> int:
    summary = autolabel_samples(
        source=Path(args.source),
        bucket=args.bucket,
        overwrite=args.overwrite,
        dry_run=args.dry_run,
        max_width=args.max_width,
    )
    _print_autolabel_summary("RPA 样本规则自动补标", summary, dry_run=args.dry_run)
    print("该命令按颜色和区域规则生成草稿标签；训练前仍建议用 LabelImg 抽查并修正误框。")
    return 0


def _cmd_yolo_autolabel_samples(args: argparse.Namespace) -> int:
    summary = yolo_autolabel_samples(
        source=Path(args.source),
        train_bucket=args.train_bucket,
        target_bucket=args.target_bucket,
        model=args.model,
        epochs=args.epochs,
        imgsz=args.imgsz,
        batch=args.batch,
        device=args.device,
        conf=args.conf,
        version=args.version,
        artifacts_root=Path(args.artifacts_root) if args.artifacts_root else None,
        overwrite_auto=args.overwrite_auto,
        dry_run=args.dry_run,
        min_train_samples=args.min_train_samples,
    )
    mode_text = "预览" if args.dry_run else "写入"
    print(
        f"YOLO 学习补录{mode_text}完成："
        f"训练样本={summary.train_images}, 目标图片={summary.target_images}, 预测命中图片={summary.predicted_images}, "
        f"更新图片={summary.updated_images}, send_button={summary.added_send_buttons}, "
        f"customer_message_bubble={summary.added_customer_bubbles}, self_message_bubble={summary.added_self_bubbles}, "
        f"替换旧框={summary.replaced_boxes}"
    )
    if summary.weights:
        print(f"辅助权重：{summary.weights}")
    if summary.report_path:
        print(f"报告：{summary.report_path}")
    for warning in summary.warnings:
        print(f"警告：{warning}")
    return 0


def _print_autolabel_summary(prefix: str, summary, *, dry_run: bool) -> None:
    if summary is None:
        return

    mode_text = "预览" if dry_run else "写入"
    print(
        f"{prefix}{mode_text}完成："
        f"图片={summary.checked_images}, 更新={summary.updated_images}, 未变={summary.unchanged_images}, "
        f"新建标签={summary.created_labels}, send_button={summary.added_send_buttons}, "
        f"customer_message_bubble={summary.added_customer_bubbles}, "
        f"self_message_bubble={summary.added_self_bubbles}"
    )
    for warning in summary.warnings:
        print(f"警告：{warning}")
    return 0


def _cmd_template(args: argparse.Namespace) -> int:
    summary = apply_template(
        dataset=Path(args.dataset),
        template=Path(args.from_label),
        overwrite=args.overwrite,
        include_send_button=args.include_send_button,
    )
    print(
        "模板应用完成："
        f"新建={summary.created}, 更新={summary.updated}, 跳过模板={summary.skipped}"
    )
    print("默认只复制 conversation_list、chat_content、input_area、input_box；消息气泡仍需按图片人工补。")
    return 0


def _cmd_validate(args: argparse.Namespace) -> int:
    result = validate_dataset(Path(args.dataset))
    print(f"已检查图片 {result.checked_images} 张，标签 {result.checked_labels} 个。")
    for warning in result.warnings:
        print(f"警告：{warning}")
    for error in result.errors:
        print(f"错误：{error}", file=sys.stderr)

    if result.ok:
        print("校验通过。")
        return 0

    print(f"校验失败：{len(result.errors)} 个错误。", file=sys.stderr)
    return 1


def _cmd_split(args: argparse.Namespace) -> int:
    summary = split_dataset(Path(args.dataset), args.val_ratio, args.test_ratio, args.seed)
    print(f"划分完成：train={summary.train}, val={summary.val}, test={summary.test}")
    return 0


def _cmd_train(args: argparse.Namespace) -> int:
    save_dir = train_model(
        dataset=Path(args.dataset),
        model=args.model,
        epochs=args.epochs,
        imgsz=args.imgsz,
        batch=args.batch,
        device=args.device,
        name=args.name,
    )
    print(f"训练完成，输出目录：{save_dir}")
    return 0


def _cmd_predict(args: argparse.Namespace) -> int:
    out = predict_images(
        weights=Path(args.weights),
        source=Path(args.source),
        out=Path(args.out),
        imgsz=args.imgsz,
        conf=args.conf,
    )
    print(f"预测完成，输出目录：{out}")
    return 0


def _cmd_export(args: argparse.Namespace) -> int:
    onnx_path = export_onnx(
        weights=Path(args.weights),
        out=Path(args.out),
        imgsz=args.imgsz,
        dataset=Path(args.dataset) if args.dataset else None,
        version=args.version,
        opset=args.opset,
        simplify=args.simplify,
    )
    print(f"ONNX 导出完成：{onnx_path}")
    return 0


def _cmd_gui(_args: argparse.Namespace) -> int:
    from .gui import main as gui_main

    return gui_main([])


def _cmd_package(args: argparse.Namespace) -> int:
    installed_dir = package_artifact(
        artifact=Path(args.artifact),
        install_local=args.install_local,
        destination=Path(args.dest) if args.dest else None,
    )
    if installed_dir:
        print(f"模型包已安装到：{installed_dir}")
    else:
        print("模型包文件完整；未指定 --install-local 或 --dest，因此未复制。")
    return 0
