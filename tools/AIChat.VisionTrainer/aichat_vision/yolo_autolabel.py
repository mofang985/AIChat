from __future__ import annotations

import json
import random
import shutil
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

from .active_learn import default_artifacts_root
from .autolabel import AUTO_CLASS_IDS, YoloBox, _format_yolo_boxes, _iou, _read_yolo_boxes, _resolve_sample_directories
from .dataset import image_files
from .errors import VisionTrainerError
from .labels import LABELS, write_data_yaml, write_labels_file
from .paths import tool_root

REPORT_FILE_NAME = "yolo-autolabel-report.json"


@dataclass
class YoloLearnAutolabelSummary:
    version: str
    train_images: int = 0
    target_images: int = 0
    predicted_images: int = 0
    updated_images: int = 0
    added_send_buttons: int = 0
    added_customer_bubbles: int = 0
    added_self_bubbles: int = 0
    replaced_boxes: int = 0
    dry_run: bool = False
    work_dir: Path | None = None
    dataset_dir: Path | None = None
    train_dir: Path | None = None
    weights: Path | None = None
    report_path: Path | None = None
    warnings: list[str] = field(default_factory=list)


def yolo_autolabel_samples(
    *,
    source: Path,
    train_bucket: str = "accepted",
    target_bucket: str = "accepted",
    model: str = "yolo11n.pt",
    epochs: int = 30,
    imgsz: int = 960,
    batch: int = 8,
    device: str | None = "0",
    conf: float = 0.35,
    version: str | None = None,
    artifacts_root: Path | None = None,
    overwrite_auto: bool = False,
    dry_run: bool = False,
    min_train_samples: int = 5,
    seed: int = 42,
) -> YoloLearnAutolabelSummary:
    if epochs <= 0:
        raise VisionTrainerError("epochs 必须大于 0。")
    if imgsz <= 0:
        raise VisionTrainerError("imgsz 必须大于 0。")
    if batch <= 0:
        raise VisionTrainerError("batch 必须大于 0。")
    if conf < 0 or conf > 1:
        raise VisionTrainerError("conf 必须在 0-1 范围内。")
    if min_train_samples < 1:
        raise VisionTrainerError("min-train-samples 必须大于等于 1。")

    source = source.resolve()
    if not source.exists():
        raise VisionTrainerError(f"RPA 学习样本目录不存在：{source}")

    train_dirs = _resolve_sample_directories(source, train_bucket)
    target_dirs = _resolve_sample_directories(source, target_bucket)
    if not train_dirs:
        raise VisionTrainerError(f"没有找到训练样本分桶：source={source}, bucket={train_bucket}")
    if not target_dirs:
        raise VisionTrainerError(f"没有找到补录目标分桶：source={source}, bucket={target_bucket}")

    train_pairs = _collect_labeled_pairs(train_dirs)
    target_images = _collect_target_images(target_dirs)
    version = version or _default_version()
    artifacts_root = (artifacts_root or default_artifacts_root()).resolve()
    work_dir = artifacts_root / "wechat-layout-yolo-autolabel" / version

    summary = YoloLearnAutolabelSummary(
        version=version,
        train_images=len(train_pairs),
        target_images=len(target_images),
        dry_run=dry_run,
        work_dir=work_dir,
        dataset_dir=work_dir / "dataset",
        report_path=None if dry_run else work_dir / REPORT_FILE_NAME,
    )

    if len(train_pairs) < min_train_samples:
        raise VisionTrainerError(
            "YOLO 学习补录训练样本不足："
            f"当前可训练={len(train_pairs)}，要求={min_train_samples}。"
        )
    if not target_images:
        raise VisionTrainerError(f"补录目标分桶没有图片：source={source}, bucket={target_bucket}")

    if dry_run:
        return summary

    _prepare_training_dataset(
        train_pairs=train_pairs,
        dataset_dir=summary.dataset_dir,
        seed=seed,
    )

    summary.train_dir = _train_assist_model(
        dataset_dir=summary.dataset_dir,
        model=model,
        epochs=epochs,
        imgsz=imgsz,
        batch=batch,
        device=device,
        work_dir=work_dir,
    )
    summary.weights = summary.train_dir / "weights" / "best.pt"
    if not summary.weights.exists():
        raise VisionTrainerError(f"YOLO 学习补录训练完成但未找到 best.pt：{summary.weights}")

    predictions = _predict_target_images(
        weights=summary.weights,
        target_images=target_images,
        imgsz=imgsz,
        conf=conf,
    )
    _apply_predictions(
        predictions=predictions,
        overwrite_auto=overwrite_auto,
        summary=summary,
    )
    _write_report(summary, source, train_bucket, target_bucket, model, epochs, imgsz, batch, device, conf, overwrite_auto)
    return summary


def _collect_labeled_pairs(directories: list[Path]) -> list[tuple[Path, Path]]:
    pairs: list[tuple[Path, Path]] = []
    seen: set[Path] = set()
    for directory in directories:
        for image_path in image_files(directory):
            resolved = image_path.resolve()
            if resolved in seen:
                continue
            seen.add(resolved)
            label_path = image_path.with_suffix(".txt")
            if not label_path.exists():
                continue
            lines = [line.strip() for line in label_path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
            if not lines:
                continue
            pairs.append((image_path, label_path))
    return pairs


def _collect_target_images(directories: list[Path]) -> list[Path]:
    images: list[Path] = []
    seen: set[Path] = set()
    for directory in directories:
        for image_path in image_files(directory):
            resolved = image_path.resolve()
            if resolved in seen:
                continue
            seen.add(resolved)
            images.append(image_path)
    return images


def _prepare_training_dataset(
    *,
    train_pairs: list[tuple[Path, Path]],
    dataset_dir: Path,
    seed: int,
) -> None:
    if dataset_dir.exists():
        shutil.rmtree(dataset_dir)

    for split in ("train", "val", "test"):
        (dataset_dir / "images" / split).mkdir(parents=True, exist_ok=True)
        (dataset_dir / "labels" / split).mkdir(parents=True, exist_ok=True)
    write_labels_file(dataset_dir / "labels.txt")
    write_data_yaml(dataset_dir / "data.yaml", dataset_dir)

    rng = random.Random(seed)
    pairs = list(train_pairs)
    rng.shuffle(pairs)
    val_count = 0 if len(pairs) == 1 else max(1, round(len(pairs) * 0.2))
    val_count = min(val_count, max(0, len(pairs) - 1))
    split_pairs = {
        "val": pairs[:val_count],
        "train": pairs[val_count:],
    }

    for split, items in split_pairs.items():
        for image_path, label_path in items:
            shutil.copy2(image_path, dataset_dir / "images" / split / image_path.name)
            shutil.copy2(label_path, dataset_dir / "labels" / split / label_path.name)


def _train_assist_model(
    *,
    dataset_dir: Path,
    model: str,
    epochs: int,
    imgsz: int,
    batch: int,
    device: str | None,
    work_dir: Path,
) -> Path:
    try:
        from ultralytics import YOLO
    except ModuleNotFoundError as exc:
        raise VisionTrainerError("缺少 ultralytics 依赖，请先执行：pip install -r requirements.txt") from exc

    runs_project = work_dir / "runs"
    yolo = YOLO(model)
    results = yolo.train(
        data=str(dataset_dir / "data.yaml"),
        epochs=epochs,
        imgsz=imgsz,
        batch=batch,
        device=device,
        project=str(runs_project),
        name="train",
        exist_ok=True,
    )
    save_dir = getattr(results, "save_dir", None)
    return Path(save_dir).resolve() if save_dir else (runs_project / "train").resolve()


def _predict_target_images(
    *,
    weights: Path,
    target_images: list[Path],
    imgsz: int,
    conf: float,
) -> dict[Path, list[YoloBox]]:
    try:
        from ultralytics import YOLO
    except ModuleNotFoundError as exc:
        raise VisionTrainerError("缺少 ultralytics 依赖，请先执行：pip install -r requirements.txt") from exc

    yolo = YOLO(str(weights))
    predictions: dict[Path, list[YoloBox]] = {}
    for image_path in target_images:
        predictions[image_path] = []
        results = yolo.predict(
            source=str(image_path),
            imgsz=imgsz,
            conf=conf,
            save=False,
            verbose=False,
        )
        if not results:
            continue
        result = results[0]
        boxes = getattr(result, "boxes", None)
        if boxes is None or len(boxes) == 0:
            continue
        class_ids = boxes.cls.tolist()
        xywhn = boxes.xywhn.tolist()
        for class_id_value, coords in zip(class_ids, xywhn):
            class_id = int(class_id_value)
            if class_id not in AUTO_CLASS_IDS:
                continue
            x_center, y_center, width, height = [float(value) for value in coords]
            predictions[image_path].append(YoloBox(class_id, x_center, y_center, width, height))
    return predictions


def _apply_predictions(
    *,
    predictions: dict[Path, list[YoloBox]],
    overwrite_auto: bool,
    summary: YoloLearnAutolabelSummary,
) -> None:
    for image_path, predicted_boxes in predictions.items():
        if not predicted_boxes:
            continue
        summary.predicted_images += 1
        label_path = image_path.with_suffix(".txt")
        if not label_path.exists():
            summary.warnings.append(f"{image_path}: 缺少同名 .txt，已跳过 YOLO 学习补录。")
            continue

        existing_boxes = _read_yolo_boxes(label_path)
        next_boxes = list(existing_boxes)
        if overwrite_auto:
            predicted_class_ids = {box.class_id for box in predicted_boxes}
            before = len(next_boxes)
            next_boxes = [box for box in next_boxes if box.class_id not in predicted_class_ids]
            summary.replaced_boxes += before - len(next_boxes)

        added = _append_predictions(next_boxes, predicted_boxes)
        if not added and not overwrite_auto:
            continue
        if not added and overwrite_auto:
            continue

        label_path.write_text(_format_yolo_boxes(next_boxes), encoding="utf-8")
        summary.updated_images += 1
        summary.added_send_buttons += sum(1 for box in added if box.class_id == 4)
        summary.added_customer_bubbles += sum(1 for box in added if box.class_id == 5)
        summary.added_self_bubbles += sum(1 for box in added if box.class_id == 6)


def _append_predictions(target_boxes: list[YoloBox], predicted_boxes: list[YoloBox]) -> list[YoloBox]:
    added: list[YoloBox] = []
    for predicted in predicted_boxes:
        if predicted.class_id not in AUTO_CLASS_IDS:
            continue
        predicted_pixel = predicted.to_pixel_box(1, 1)
        if any(
            existing.class_id == predicted.class_id
            and _iou(existing.to_pixel_box(1, 1), predicted_pixel) >= 0.35
            for existing in target_boxes
        ):
            continue
        target_boxes.append(predicted)
        added.append(predicted)
    target_boxes.sort(key=lambda box: (box.class_id, box.y_center, box.x_center))
    return added


def _write_report(
    summary: YoloLearnAutolabelSummary,
    source: Path,
    train_bucket: str,
    target_bucket: str,
    model: str,
    epochs: int,
    imgsz: int,
    batch: int,
    device: str | None,
    conf: float,
    overwrite_auto: bool,
) -> None:
    assert summary.report_path is not None
    summary.report_path.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "version": summary.version,
        "source": str(source),
        "trainBucket": train_bucket,
        "targetBucket": target_bucket,
        "model": model,
        "epochs": epochs,
        "imgsz": imgsz,
        "batch": batch,
        "device": device,
        "conf": conf,
        "overwriteAuto": overwrite_auto,
        "summary": {
            "trainImages": summary.train_images,
            "targetImages": summary.target_images,
            "predictedImages": summary.predicted_images,
            "updatedImages": summary.updated_images,
            "addedSendButtons": summary.added_send_buttons,
            "addedCustomerBubbles": summary.added_customer_bubbles,
            "addedSelfBubbles": summary.added_self_bubbles,
            "replacedBoxes": summary.replaced_boxes,
            "warnings": summary.warnings,
        },
        "paths": {
            "workDir": str(summary.work_dir) if summary.work_dir else None,
            "datasetDir": str(summary.dataset_dir) if summary.dataset_dir else None,
            "trainDir": str(summary.train_dir) if summary.train_dir else None,
            "weights": str(summary.weights) if summary.weights else None,
        },
    }
    summary.report_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def _default_version() -> str:
    return "yolo-autolabel-" + datetime.now().strftime("%Y%m%d-%H%M%S")
