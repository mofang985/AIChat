from __future__ import annotations

import hashlib
import shutil
from dataclasses import dataclass
from pathlib import Path

from .dataset import IMAGE_EXTENSIONS, ensure_dataset_structure, image_files
from .errors import VisionTrainerError


@dataclass(frozen=True)
class RpaIngestSummary:
    scanned: int
    imported: int
    skipped_duplicates: int
    skipped_missing_labels: int
    skipped_empty_labels: int
    updated_duplicate_labels: int = 0


def ingest_rpa_samples(source: Path, dataset: Path, bucket: str = "review") -> RpaIngestSummary:
    source = source.resolve()
    dataset = dataset.resolve()
    if not source.exists():
        raise VisionTrainerError(f"RPA 学习样本目录不存在：{source}")

    if bucket not in {"review", "accepted", "fixed", "accepted-fixed", "all"}:
        raise VisionTrainerError("bucket 只能是 review、accepted、fixed、accepted-fixed 或 all。")

    ensure_dataset_structure(dataset)
    raw_dir = dataset / "raw"
    existing_images = _collect_existing_images(raw_dir)

    scanned = 0
    imported = 0
    skipped_duplicates = 0
    skipped_missing_labels = 0
    skipped_empty_labels = 0
    updated_duplicate_labels = 0

    for sample_dir in _resolve_sample_directories(source, bucket):
        for image_path in sorted(path for path in sample_dir.iterdir() if _is_image(path)):
            scanned += 1
            label_path = image_path.with_suffix(".txt")
            if not label_path.exists():
                skipped_missing_labels += 1
                continue

            label_text = label_path.read_text(encoding="utf-8-sig").strip()
            if not label_text:
                skipped_empty_labels += 1
                continue

            image_hash = _file_sha256(image_path)
            existing_image = existing_images.get(image_hash)
            if existing_image is not None:
                skipped_duplicates += 1
                existing_label = existing_image.with_suffix(".txt")
                if not existing_label.exists() or existing_label.read_text(encoding="utf-8-sig").strip() != label_text:
                    existing_label.write_text(label_text + "\n", encoding="utf-8")
                    updated_duplicate_labels += 1

                metadata_path = image_path.with_suffix(".json")
                if metadata_path.exists():
                    shutil.copy2(metadata_path, existing_image.with_suffix(".json"))

                continue

            target_image = _unique_target_path(raw_dir, image_path.name, image_hash)
            target_label = target_image.with_suffix(".txt")
            target_metadata = target_image.with_suffix(".json")

            shutil.copy2(image_path, target_image)
            target_label.write_text(label_text + "\n", encoding="utf-8")

            metadata_path = image_path.with_suffix(".json")
            if metadata_path.exists():
                shutil.copy2(metadata_path, target_metadata)

            existing_images[image_hash] = target_image
            imported += 1

    return RpaIngestSummary(
        scanned=scanned,
        imported=imported,
        skipped_duplicates=skipped_duplicates,
        skipped_missing_labels=skipped_missing_labels,
        skipped_empty_labels=skipped_empty_labels,
        updated_duplicate_labels=updated_duplicate_labels,
    )


def _resolve_sample_directories(source: Path, bucket: str) -> list[Path]:
    if bucket == "accepted-fixed":
        directories = [source / name for name in ("accepted", "fixed") if (source / name).is_dir()]
        return directories or [source]

    if bucket == "all":
        directories = [source / name for name in ("accepted", "fixed", "review") if (source / name).is_dir()]
        return directories or [source]

    candidate = source / bucket
    return [candidate] if candidate.is_dir() else [source]


def _collect_existing_images(raw_dir: Path) -> dict[str, Path]:
    return {_file_sha256(path): path for path in image_files(raw_dir)}


def _unique_target_path(raw_dir: Path, file_name: str, image_hash: str) -> Path:
    target = raw_dir / file_name
    if not target.exists():
        return target

    suffix = target.suffix
    stem = target.stem
    candidate = raw_dir / f"{stem}-{image_hash[:10]}{suffix}"
    counter = 1
    while candidate.exists():
        candidate = raw_dir / f"{stem}-{image_hash[:10]}-{counter}{suffix}"
        counter += 1

    return candidate


def _is_image(path: Path) -> bool:
    return path.is_file() and path.suffix.lower() in IMAGE_EXTENSIONS


def _file_sha256(path: Path) -> str:
    hasher = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            hasher.update(chunk)
    return hasher.hexdigest()
