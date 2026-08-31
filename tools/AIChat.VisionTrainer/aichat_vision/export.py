from __future__ import annotations

import csv
import json
import shutil
from datetime import datetime
from pathlib import Path

from .dataset import dataset_summary
from .errors import VisionTrainerError
from .labels import LABELS, LABELS_FILE_NAME, MODEL_CODE, MODEL_VERSION_FILE_NAME, ONNX_FILE_NAME, write_labels_file


def export_onnx(
    weights: Path,
    out: Path,
    imgsz: int,
    dataset: Path | None = None,
    version: str = "m4.2-001",
    opset: int | None = None,
    simplify: bool = False,
) -> Path:
    try:
        from ultralytics import YOLO
    except ModuleNotFoundError as exc:
        raise VisionTrainerError("缺少 ultralytics 依赖，请先执行：pip install -r requirements.txt") from exc

    weights = weights.resolve()
    out = out.resolve()
    if not weights.exists():
        raise VisionTrainerError(f"权重文件不存在：{weights}")

    out.mkdir(parents=True, exist_ok=True)
    yolo = YOLO(str(weights))
    kwargs: dict[str, object] = {
        "format": "onnx",
        "imgsz": imgsz,
        "simplify": simplify,
    }
    if opset is not None:
        kwargs["opset"] = opset

    exported = Path(yolo.export(**kwargs)).resolve()
    if not exported.exists():
        raise VisionTrainerError(f"ONNX 导出失败，未找到导出文件：{exported}")

    onnx_target = out / ONNX_FILE_NAME
    if exported != onnx_target:
        shutil.copy2(exported, onnx_target)

    shutil.copy2(weights, out / "best.pt")
    write_labels_file(out / LABELS_FILE_NAME)
    _write_model_version(weights, out / MODEL_VERSION_FILE_NAME, imgsz, dataset, version)
    return onnx_target


def _write_model_version(weights: Path, target: Path, imgsz: int, dataset: Path | None, version: str) -> None:
    payload = {
        "modelCode": MODEL_CODE,
        "version": version,
        "trainedAt": datetime.now().astimezone().isoformat(timespec="seconds"),
        "inputSize": imgsz,
        "labels": list(LABELS),
        "datasetSummary": dataset_summary(dataset.resolve()) if dataset else {
            "trainImages": 0,
            "valImages": 0,
            "testImages": 0,
        },
        "metrics": _read_metrics(weights),
    }
    target.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def _read_metrics(weights: Path) -> dict[str, float | None]:
    run_dir = weights.parent.parent if weights.parent.name.lower() == "weights" else weights.parent
    results_csv = run_dir / "results.csv"
    metrics: dict[str, float | None] = {
        "map50": None,
        "map50_95": None,
    }
    if not results_csv.exists():
        return metrics

    try:
        with results_csv.open("r", encoding="utf-8", newline="") as stream:
            rows = list(csv.DictReader(stream))
    except Exception:
        return metrics

    if not rows:
        return metrics

    last = {key.strip(): value for key, value in rows[-1].items()}
    for key, value in last.items():
        normalized = key.lower().replace(" ", "")
        if normalized in {"metrics/map50(b)", "metrics/map50"}:
            metrics["map50"] = _to_float(value)
        elif normalized in {"metrics/map50-95(b)", "metrics/map50_95", "metrics/map"}:
            metrics["map50_95"] = _to_float(value)

    return metrics


def _to_float(value: str | None) -> float | None:
    if value is None or value == "":
        return None
    try:
        return float(value)
    except ValueError:
        return None
