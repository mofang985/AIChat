from __future__ import annotations

from dataclasses import dataclass
from pathlib import Path

from .errors import VisionTrainerError
from .package import PACKAGE_FILES, copy_package_files, default_candidate_root, default_local_install_dir


@dataclass(frozen=True)
class PromoteSummary:
    candidate_dir: Path
    installed_dir: Path | None
    copied_files: tuple[str, ...]


def promote_candidate(
    candidate: str,
    install_local: bool = False,
    candidate_root: Path | None = None,
    destination: Path | None = None,
) -> PromoteSummary:
    candidate_dir = resolve_candidate_dir(candidate, candidate_root)
    _validate_candidate(candidate_dir)

    target_dir = destination.resolve() if destination else (default_local_install_dir() if install_local else None)
    if target_dir is not None:
        copy_package_files(candidate_dir, target_dir)

    return PromoteSummary(
        candidate_dir=candidate_dir,
        installed_dir=target_dir,
        copied_files=PACKAGE_FILES if target_dir is not None else tuple(),
    )


def resolve_candidate_dir(candidate: str, candidate_root: Path | None = None) -> Path:
    candidate_path = Path(candidate).expanduser()
    looks_like_path = candidate_path.is_absolute() or len(candidate_path.parts) > 1
    if looks_like_path or candidate_path.exists():
        return candidate_path.resolve()

    root = candidate_root.resolve() if candidate_root else default_candidate_root()
    return (root / candidate).resolve()


def _validate_candidate(candidate_dir: Path) -> None:
    if not candidate_dir.exists():
        raise VisionTrainerError(f"候选模型目录不存在：{candidate_dir}")

    missing = [name for name in PACKAGE_FILES if not (candidate_dir / name).exists()]
    if missing:
        raise VisionTrainerError(f"候选模型不完整，缺少：{', '.join(missing)}")
