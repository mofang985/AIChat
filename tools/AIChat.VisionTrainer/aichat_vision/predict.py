from __future__ import annotations

from pathlib import Path

from .errors import VisionTrainerError


def predict_images(weights: Path, source: Path, out: Path, imgsz: int | None = None, conf: float = 0.25) -> Path:
    try:
        from ultralytics import YOLO
    except ModuleNotFoundError as exc:
        raise VisionTrainerError("缺少 ultralytics 依赖，请先执行：pip install -r requirements.txt") from exc

    weights = weights.resolve()
    source = source.resolve()
    out = out.resolve()
    if not weights.exists():
        raise VisionTrainerError(f"权重文件不存在：{weights}")
    if not source.exists():
        raise VisionTrainerError(f"预测来源不存在：{source}")

    out.parent.mkdir(parents=True, exist_ok=True)
    yolo = YOLO(str(weights))
    kwargs: dict[str, object] = {
        "source": str(source),
        "save": True,
        "project": str(out.parent),
        "name": out.name,
        "exist_ok": True,
        "conf": conf,
    }
    if imgsz is not None:
        kwargs["imgsz"] = imgsz

    yolo.predict(**kwargs)
    return out
