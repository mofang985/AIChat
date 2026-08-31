from __future__ import annotations

import os
import shutil
from pathlib import Path

from .errors import VisionTrainerError
from .labels import LABELS_FILE_NAME, MODEL_VERSION_FILE_NAME, ONNX_FILE_NAME

PACKAGE_FILES = (ONNX_FILE_NAME, LABELS_FILE_NAME, MODEL_VERSION_FILE_NAME)


def default_model_root() -> Path:
    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        base = Path(local_app_data)
    else:
        base = Path.home() / "AppData" / "Local"

    return base / "AIChat" / "RpaClient" / "models"


def default_local_install_dir() -> Path:
    return default_model_root() / "wechat-layout"


def default_candidate_root() -> Path:
    return default_model_root() / "wechat-layout-candidates"


def package_artifact(artifact: Path, install_local: bool = False, destination: Path | None = None) -> Path | None:
    artifact = artifact.resolve()
    if not artifact.exists():
        raise VisionTrainerError(f"模型产物目录不存在：{artifact}")

    missing = [name for name in PACKAGE_FILES if not (artifact / name).exists()]
    if missing:
        raise VisionTrainerError(f"模型产物不完整，缺少：{', '.join(missing)}")

    target_dir = destination.resolve() if destination else (default_local_install_dir() if install_local else None)
    if target_dir is None:
        return None

    copy_package_files(artifact, target_dir)

    return target_dir


def copy_package_files(artifact: Path, target_dir: Path) -> None:
    target_dir.mkdir(parents=True, exist_ok=True)
    for file_name in PACKAGE_FILES:
        source = (artifact / file_name).resolve()
        target = (target_dir / file_name).resolve()
        if source == target:
            continue
        shutil.copy2(source, target)
