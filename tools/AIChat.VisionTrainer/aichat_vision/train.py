from __future__ import annotations

from pathlib import Path

from .dataset import ensure_dataset_structure
from .errors import VisionTrainerError
from .paths import tool_root


def train_model(
    dataset: Path,
    model: str,
    epochs: int,
    imgsz: int,
    batch: int,
    device: str | None,
    name: str = "train",
) -> Path:
    try:
        from ultralytics import YOLO
    except ModuleNotFoundError as exc:
        raise VisionTrainerError("缺少 ultralytics 依赖，请先执行：pip install -r requirements.txt") from exc

    dataset = dataset.resolve()
    ensure_dataset_structure(dataset)
    data_yaml = dataset / "data.yaml"
    runs_project = tool_root() / "runs" / "detect"

    yolo = YOLO(model)
    results = yolo.train(
        data=str(data_yaml),
        epochs=epochs,
        imgsz=imgsz,
        batch=batch,
        device=device,
        project=str(runs_project),
        name=name,
    )

    save_dir = getattr(results, "save_dir", None)
    return Path(save_dir).resolve() if save_dir else (runs_project / name).resolve()
