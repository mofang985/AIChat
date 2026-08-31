from __future__ import annotations

from pathlib import Path

MODEL_CODE = "wechat-layout"
ONNX_FILE_NAME = "wechat-layout.onnx"
LABELS_FILE_NAME = "labels.txt"
MODEL_VERSION_FILE_NAME = "model-version.json"

LABELS: tuple[str, ...] = (
    "conversation_list",
    "chat_content",
    "input_area",
    "input_box",
    "send_button",
    "customer_message_bubble",
    "self_message_bubble",
)


def labels_text() -> str:
    return "\n".join(LABELS) + "\n"


def data_yaml_text(dataset: Path) -> str:
    dataset_path = dataset.resolve().as_posix()
    names = "\n".join(f"  {index}: {label}" for index, label in enumerate(LABELS))
    return (
        f"path: {dataset_path}\n"
        "train: images/train\n"
        "val: images/val\n"
        "test: images/test\n\n"
        "names:\n"
        f"{names}\n"
    )


def write_labels_file(path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(labels_text(), encoding="utf-8")


def write_data_yaml(path: Path, dataset: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(data_yaml_text(dataset), encoding="utf-8")
