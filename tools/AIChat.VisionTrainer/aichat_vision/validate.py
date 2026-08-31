from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path

from .dataset import IMAGE_EXTENSIONS, SPLITS, image_files
from .errors import VisionTrainerError
from .labels import LABELS, LABELS_FILE_NAME


@dataclass
class ValidationResult:
    checked_images: int = 0
    checked_labels: int = 0
    errors: list[str] = field(default_factory=list)
    warnings: list[str] = field(default_factory=list)

    @property
    def ok(self) -> bool:
        return not self.errors


def validate_dataset(dataset: Path) -> ValidationResult:
    dataset = dataset.resolve()
    result = ValidationResult()
    if not dataset.exists():
        raise VisionTrainerError(f"数据集目录不存在：{dataset}")

    _validate_labels_manifest(dataset, result)
    _validate_image_group(dataset / "raw", dataset / "raw", "raw", result)
    for split in SPLITS:
        _validate_image_group(dataset / "images" / split, dataset / "labels" / split, split, result)

    _validate_orphan_labels(dataset, result)
    _validate_unassigned_raw_files(dataset, result)
    return result


def _validate_labels_manifest(dataset: Path, result: ValidationResult) -> None:
    labels_path = dataset / LABELS_FILE_NAME
    if not labels_path.exists():
        result.errors.append(f"缺少默认标签文件：{labels_path}")
        return

    labels = [
        line.strip()
        for line in labels_path.read_text(encoding="utf-8").splitlines()
        if line.strip()
    ]
    if labels != list(LABELS):
        result.errors.append(f"标签顺序与 M4.2 固定顺序不一致：{labels_path}")


def _validate_image_group(image_dir: Path, label_dir: Path, scope: str, result: ValidationResult) -> None:
    for image_path in image_files(image_dir):
        result.checked_images += 1
        _validate_image_readable(image_path, result)

        label_path = label_dir / f"{image_path.stem}.txt"
        if not label_path.exists():
            result.errors.append(f"{scope}: 图片缺少同名标签文件：{image_path}")
            continue

        _validate_label_file(label_path, scope, result)


def _validate_image_readable(image_path: Path, result: ValidationResult) -> None:
    try:
        from PIL import Image
    except ModuleNotFoundError as exc:
        raise VisionTrainerError("缺少 Pillow 依赖，请先执行：pip install -r requirements.txt") from exc

    try:
        with Image.open(image_path) as image:
            image.verify()
    except Exception as exc:  # noqa: BLE001
        result.errors.append(f"图片不可读取：{image_path}，原因：{exc}")


def _validate_label_file(label_path: Path, scope: str, result: ValidationResult) -> None:
    result.checked_labels += 1
    try:
        lines = label_path.read_text(encoding="utf-8").splitlines()
    except UnicodeDecodeError:
        result.errors.append(f"{scope}: 标签文件不是 UTF-8 文本：{label_path}")
        return

    non_empty_lines = [line.strip() for line in lines if line.strip()]
    if not non_empty_lines:
        result.errors.append(f"{scope}: 标签文件为空：{label_path}")
        return

    for line_number, line in enumerate(non_empty_lines, start=1):
        parts = line.split()
        if len(parts) != 5:
            result.errors.append(f"{scope}: 标签行不是 5 列：{label_path}:{line_number}")
            continue

        class_text, *coordinate_texts = parts
        try:
            class_id = int(class_text)
        except ValueError:
            result.errors.append(f"{scope}: class_id 不是整数：{label_path}:{line_number}")
            continue

        if class_id < 0 or class_id >= len(LABELS):
            result.errors.append(f"{scope}: class_id 越界：{label_path}:{line_number} -> {class_id}")

        try:
            x_center, y_center, width, height = [float(value) for value in coordinate_texts]
        except ValueError:
            result.errors.append(f"{scope}: 坐标不是数字：{label_path}:{line_number}")
            continue

        values = (x_center, y_center, width, height)
        if any(value < 0 or value > 1 for value in values):
            result.errors.append(f"{scope}: 坐标不在 0-1 范围：{label_path}:{line_number}")
        if width <= 0 or height <= 0:
            result.errors.append(f"{scope}: width/height 必须大于 0：{label_path}:{line_number}")
        if x_center - width / 2 < 0 or x_center + width / 2 > 1 or y_center - height / 2 < 0 or y_center + height / 2 > 1:
            result.warnings.append(f"{scope}: 标注框超出图片归一化边界：{label_path}:{line_number}")


def _validate_orphan_labels(dataset: Path, result: ValidationResult) -> None:
    raw_dir = dataset / "raw"
    raw_image_stems = {path.stem.lower() for path in image_files(raw_dir)}
    for label_path in sorted(raw_dir.rglob("*.txt")) if raw_dir.exists() else []:
        if label_path.name.lower() == "classes.txt":
            continue
        if label_path.stem.lower() not in raw_image_stems:
            result.errors.append(f"raw: 标签文件缺少同名图片：{label_path}")

    for split in SPLITS:
        image_dir = dataset / "images" / split
        label_dir = dataset / "labels" / split
        image_stems = {path.stem.lower() for path in image_files(image_dir)}
        for label_path in sorted(label_dir.glob("*.txt")) if label_dir.exists() else []:
            if label_path.stem.lower() not in image_stems:
                result.errors.append(f"{split}: 标签文件缺少同名图片：{label_path}")


def _validate_unassigned_raw_files(dataset: Path, result: ValidationResult) -> None:
    raw_images = {path.name.lower() for path in image_files(dataset / "raw")}
    if not raw_images:
        return

    assigned_images: set[str] = set()
    for split in SPLITS:
        assigned_images.update(path.name.lower() for path in image_files(dataset / "images" / split))

    unassigned = sorted(raw_images - assigned_images)
    if unassigned:
        result.warnings.append(f"raw 中有 {len(unassigned)} 张图片尚未纳入 train/val/test。")


def matching_image_exists(directory: Path, stem: str) -> bool:
    return any((directory / f"{stem}{extension}").exists() for extension in IMAGE_EXTENSIONS)
