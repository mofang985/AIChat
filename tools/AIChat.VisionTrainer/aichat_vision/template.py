from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from .dataset import image_files
from .errors import VisionTrainerError
from .labels import LABELS

DEFAULT_TEMPLATE_LABELS = {
    "conversation_list",
    "chat_content",
    "input_area",
    "input_box",
}


@dataclass(frozen=True)
class ApplyTemplateSummary:
    updated: int
    skipped: int
    created: int


def apply_template(
    dataset: Path,
    template: Path,
    overwrite: bool = False,
    include_send_button: bool = False,
) -> ApplyTemplateSummary:
    dataset = dataset.resolve()
    template = template.resolve()
    raw_dir = dataset / "raw"
    if not raw_dir.exists():
        raise VisionTrainerError(f"raw 目录不存在：{raw_dir}")
    if not template.exists():
        raise VisionTrainerError(f"模板标签文件不存在：{template}")

    template_records = _read_yolo_records(template)
    allowed_labels = set(DEFAULT_TEMPLATE_LABELS)
    if include_send_button:
        allowed_labels.add("send_button")

    template_lines = [
        record.line
        for record in template_records
        if record.label_name in allowed_labels
    ]
    if not template_lines:
        raise VisionTrainerError("模板文件中没有可复制的大区域标签。")

    created = 0
    updated = 0
    skipped = 0
    for image_path in image_files(raw_dir):
        label_path = image_path.with_suffix(".txt")
        if label_path.resolve() == template:
            skipped += 1
            continue

        existing_records = _read_yolo_records(label_path) if label_path.exists() else []
        existing_keep_lines = [
            record.line
            for record in existing_records
            if record.label_name not in allowed_labels
        ]

        new_lines = template_lines + existing_keep_lines
        label_path.write_text("".join(line.rstrip() + "\n" for line in new_lines), encoding="utf-8")
        if existing_records:
            updated += 1
        else:
            created += 1

    return ApplyTemplateSummary(updated=updated, skipped=skipped, created=created)


@dataclass(frozen=True)
class YoloRecord:
    label_name: str
    line: str


def _read_yolo_records(path: Path) -> list[YoloRecord]:
    if not path.exists():
        return []

    records: list[YoloRecord] = []
    for line in path.read_text(encoding="utf-8").splitlines():
        stripped = line.strip()
        if not stripped:
            continue

        parts = stripped.split()
        if len(parts) != 5:
            continue

        try:
            class_id = int(parts[0])
        except ValueError:
            continue

        label_name = LABELS[class_id] if 0 <= class_id < len(LABELS) else f"class_{class_id}"
        records.append(YoloRecord(label_name=label_name, line=stripped))

    return records
