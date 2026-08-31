from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from .dataset import image_files
from .errors import VisionTrainerError

# 第一版预标注只写稳定的大区域。消息气泡差异大，仍建议人工标注或训练初版模型后再辅助修正。
DEFAULT_BOXES: tuple[tuple[int, float, float, float, float], ...] = (
    (0, 0.145000, 0.500000, 0.250000, 0.960000),
    (1, 0.620000, 0.420000, 0.700000, 0.620000),
    (2, 0.620000, 0.865000, 0.700000, 0.220000),
    (3, 0.600000, 0.850000, 0.620000, 0.120000),
)

SEND_BUTTON_BOX = (4, 0.935000, 0.930000, 0.055000, 0.035000)


@dataclass(frozen=True)
class PrelabelSummary:
    created: int
    skipped: int
    overwritten: int


def prelabel_dataset(dataset: Path, overwrite: bool = False, include_send_button: bool = False) -> PrelabelSummary:
    dataset = dataset.resolve()
    raw_dir = dataset / "raw"
    if not raw_dir.exists():
        raise VisionTrainerError(f"raw 目录不存在，请先执行 init 或 capture：{raw_dir}")

    boxes = list(DEFAULT_BOXES)
    if include_send_button:
        boxes.append(SEND_BUTTON_BOX)

    created = 0
    skipped = 0
    overwritten = 0
    for image_path in image_files(raw_dir):
        label_path = image_path.with_suffix(".txt")
        if label_path.exists() and not overwrite:
            skipped += 1
            continue

        if label_path.exists():
            overwritten += 1
        else:
            created += 1

        label_path.write_text(_format_boxes(boxes), encoding="utf-8")

    if created == 0 and overwritten == 0 and skipped == 0:
        raise VisionTrainerError(f"raw 目录没有找到可预标注图片：{raw_dir}")

    return PrelabelSummary(created=created, skipped=skipped, overwritten=overwritten)


def _format_boxes(boxes: list[tuple[int, float, float, float, float]]) -> str:
    return "".join(
        f"{class_id} {x_center:.6f} {y_center:.6f} {width:.6f} {height:.6f}\n"
        for class_id, x_center, y_center, width, height in boxes
    )
