from __future__ import annotations

import random
import shutil
from dataclasses import dataclass
from pathlib import Path

from .errors import VisionTrainerError
from .labels import LABELS_FILE_NAME, write_data_yaml, write_labels_file

IMAGE_EXTENSIONS = {".bmp", ".jpeg", ".jpg", ".png", ".webp"}
SPLITS = ("train", "val", "test")


@dataclass(frozen=True)
class SplitSummary:
    train: int
    val: int
    test: int


def ensure_dataset_structure(dataset: Path) -> None:
    dataset = dataset.resolve()
    (dataset / "raw").mkdir(parents=True, exist_ok=True)
    for split in SPLITS:
        (dataset / "images" / split).mkdir(parents=True, exist_ok=True)
        (dataset / "labels" / split).mkdir(parents=True, exist_ok=True)

    write_labels_file(dataset / LABELS_FILE_NAME)
    write_labels_file(dataset / "raw" / "classes.txt")
    write_data_yaml(dataset / "data.yaml", dataset)


def image_files(directory: Path) -> list[Path]:
    if not directory.exists():
        return []

    return sorted(
        path
        for path in directory.rglob("*")
        if path.is_file() and path.suffix.lower() in IMAGE_EXTENSIONS
    )


def find_raw_pairs(dataset: Path) -> list[tuple[Path, Path]]:
    raw_dir = dataset / "raw"
    pairs: list[tuple[Path, Path]] = []
    seen_names: set[str] = set()

    for image_path in image_files(raw_dir):
        if image_path.name.lower() in seen_names:
            raise VisionTrainerError(f"raw 中存在重复图片文件名，无法稳定划分：{image_path.name}")

        seen_names.add(image_path.name.lower())
        label_path = image_path.with_suffix(".txt")
        if label_path.exists():
            pairs.append((image_path, label_path))

    return pairs


def split_dataset(dataset: Path, val_ratio: float, test_ratio: float, seed: int = 42) -> SplitSummary:
    if val_ratio < 0 or test_ratio < 0 or val_ratio + test_ratio >= 1:
        raise VisionTrainerError("val-ratio 和 test-ratio 必须大于等于 0，且二者之和必须小于 1。")

    ensure_dataset_structure(dataset)
    pairs = find_raw_pairs(dataset)
    if not pairs:
        raise VisionTrainerError("raw 目录中没有找到已标注图片；请确认图片旁存在同名 .txt 标签文件。")

    _clear_split_directories(dataset)

    rng = random.Random(seed)
    rng.shuffle(pairs)

    total = len(pairs)
    test_count = round(total * test_ratio)
    val_count = round(total * val_ratio)
    train_count = total - val_count - test_count

    buckets = {
        "test": pairs[:test_count],
        "val": pairs[test_count:test_count + val_count],
        "train": pairs[test_count + val_count:],
    }

    for split, split_pairs in buckets.items():
        image_target_dir = dataset / "images" / split
        label_target_dir = dataset / "labels" / split
        image_target_dir.mkdir(parents=True, exist_ok=True)
        label_target_dir.mkdir(parents=True, exist_ok=True)
        for image_path, label_path in split_pairs:
            shutil.copy2(image_path, image_target_dir / image_path.name)
            shutil.copy2(label_path, label_target_dir / label_path.name)

    return SplitSummary(train=train_count, val=val_count, test=test_count)


def _clear_split_directories(dataset: Path) -> None:
    dataset = dataset.resolve()
    for split in SPLITS:
        for kind in ("images", "labels"):
            target_dir = (dataset / kind / split).resolve()
            expected_parent = (dataset / kind).resolve()
            if target_dir.parent != expected_parent:
                raise VisionTrainerError(f"拒绝清理非预期目录：{target_dir}")

            target_dir.mkdir(parents=True, exist_ok=True)
            for path in target_dir.iterdir():
                if path.is_file():
                    path.unlink()


def dataset_summary(dataset: Path) -> dict[str, int]:
    return {
        "trainImages": len(image_files(dataset / "images" / "train")),
        "valImages": len(image_files(dataset / "images" / "val")),
        "testImages": len(image_files(dataset / "images" / "test")),
    }
