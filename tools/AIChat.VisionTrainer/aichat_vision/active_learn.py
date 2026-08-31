from __future__ import annotations

import hashlib
import json
import random
import shutil
from dataclasses import dataclass, field
from datetime import datetime
from pathlib import Path

from .autolabel import AutolabelSummary, autolabel_samples
from .dataset import IMAGE_EXTENSIONS, SplitSummary, image_files, split_dataset
from .errors import VisionTrainerError
from .export import export_onnx
from .labels import LABELS_FILE_NAME, MODEL_VERSION_FILE_NAME, ONNX_FILE_NAME, write_labels_file
from .package import default_candidate_root
from .paths import tool_root
from .predict import predict_images
from .rpa_ingest import RpaIngestSummary, ingest_rpa_samples
from .train import train_model
from .validate import ValidationResult, validate_dataset

ACTIVE_LEARN_REPORT_FILE_NAME = "active-learn-report.json"
CANDIDATE_FILES = (
    ONNX_FILE_NAME,
    LABELS_FILE_NAME,
    MODEL_VERSION_FILE_NAME,
    "best.pt",
    ACTIVE_LEARN_REPORT_FILE_NAME,
)


@dataclass(frozen=True)
class RpaSample:
    bucket: str
    image_path: Path
    label_path: Path
    metadata_path: Path | None
    image_hash: str


@dataclass(frozen=True)
class SampleInventory:
    scanned_images: int
    usable_samples: int
    skipped_duplicates: int
    skipped_missing_labels: int
    skipped_empty_labels: int
    bucket_counts: dict[str, int]
    samples: tuple[RpaSample, ...] = field(repr=False)


@dataclass
class ActiveLearnSummary:
    version: str
    source: Path
    dataset: Path
    min_samples: int
    review_count: int
    bucket: str
    model: str
    epochs: int
    imgsz: int
    batch: int
    device: str | None
    val_ratio: float
    test_ratio: float
    seed: int
    predict_conf: float
    inventory: SampleInventory
    ingest: RpaIngestSummary | None = None
    review_dir: Path | None = None
    review_manifest: Path | None = None
    validation_before_split: ValidationResult | None = None
    split: SplitSummary | None = None
    validation_after_split: ValidationResult | None = None
    train_dir: Path | None = None
    weights: Path | None = None
    predict_dir: Path | None = None
    artifact_dir: Path | None = None
    candidate_install_dir: Path | None = None
    report_path: Path | None = None
    dry_run: bool = False
    skip_train: bool = False
    autolabel_summary: AutolabelSummary | None = None
    autolabel_samples_enabled: bool = True
    autolabel_overwrite: bool = False
    autolabel_max_width: int = 1280


def run_active_learning(
    *,
    source: Path,
    dataset: Path,
    min_samples: int = 1000,
    review_count: int = 50,
    bucket: str = "accepted",
    model: str = "yolo11n.pt",
    epochs: int = 80,
    imgsz: int = 960,
    batch: int = 8,
    device: str | None = "0",
    version: str | None = None,
    val_ratio: float = 0.2,
    test_ratio: float = 0.1,
    seed: int = 42,
    predict_conf: float = 0.15,
    artifacts_root: Path | None = None,
    candidate_root: Path | None = None,
    install_candidate: bool = True,
    skip_train: bool = False,
    dry_run: bool = False,
    opset: int | None = None,
    simplify: bool = False,
    autolabel_samples_enabled: bool = True,
    autolabel_overwrite: bool = False,
    autolabel_max_width: int = 1280,
) -> ActiveLearnSummary:
    if min_samples < 0:
        raise VisionTrainerError("min-samples 不能小于 0。")
    if review_count < 0:
        raise VisionTrainerError("review-count 不能小于 0。")
    if bucket not in {"review", "accepted", "fixed", "accepted-fixed", "all"}:
        raise VisionTrainerError("bucket 只能是 review、accepted、fixed、accepted-fixed 或 all。")

    version = version or _default_version()
    source = source.resolve()
    dataset = dataset.resolve()
    artifacts_root = (artifacts_root or default_artifacts_root()).resolve()

    autolabel_summary = None
    if autolabel_samples_enabled:
        autolabel_summary = autolabel_samples(
            source=source,
            bucket=bucket,
            overwrite=autolabel_overwrite,
            dry_run=dry_run,
            max_width=autolabel_max_width,
        )

    inventory = collect_sample_inventory(source, bucket)
    summary = ActiveLearnSummary(
        version=version,
        source=source,
        dataset=dataset,
        min_samples=min_samples,
        review_count=review_count,
        bucket=bucket,
        model=model,
        epochs=epochs,
        imgsz=imgsz,
        batch=batch,
        device=device,
        val_ratio=val_ratio,
        test_ratio=test_ratio,
        seed=seed,
        predict_conf=predict_conf,
        inventory=inventory,
        dry_run=dry_run,
        skip_train=skip_train,
        autolabel_summary=autolabel_summary,
        autolabel_samples_enabled=autolabel_samples_enabled,
        autolabel_overwrite=autolabel_overwrite,
        autolabel_max_width=autolabel_max_width,
    )

    if inventory.usable_samples < min_samples:
        raise VisionTrainerError(
            "主动学习样本不足，暂不训练："
            f"当前可用={inventory.usable_samples}，要求={min_samples}。"
        )

    if dry_run:
        return summary

    summary.review_dir = artifacts_root / "wechat-layout-review" / version
    summary.review_manifest = create_review_pack(
        samples=inventory.samples,
        out_dir=summary.review_dir,
        review_count=review_count,
        seed=seed,
        version=version,
        source=source,
    )

    summary.ingest = ingest_rpa_samples(source=source, dataset=dataset, bucket=bucket)

    summary.validation_before_split = validate_dataset(dataset)
    _ensure_validation_ok("导入后校验", summary.validation_before_split)

    summary.split = split_dataset(dataset, val_ratio=val_ratio, test_ratio=test_ratio, seed=seed)

    summary.validation_after_split = validate_dataset(dataset)
    _ensure_validation_ok("划分后校验", summary.validation_after_split)

    summary.artifact_dir = artifacts_root / "wechat-layout-candidates" / version
    summary.report_path = summary.artifact_dir / ACTIVE_LEARN_REPORT_FILE_NAME
    if skip_train:
        _write_report(summary)
        return summary

    summary.train_dir = train_model(
        dataset=dataset,
        model=model,
        epochs=epochs,
        imgsz=imgsz,
        batch=batch,
        device=device,
        name=version,
    )
    summary.weights = summary.train_dir / "weights" / "best.pt"
    if not summary.weights.exists():
        raise VisionTrainerError(f"训练完成但未找到 best.pt：{summary.weights}")

    test_source = dataset / "images" / "test"
    if image_files(test_source):
        summary.predict_dir = artifacts_root / "wechat-layout-predict" / version
        predict_images(
            weights=summary.weights,
            source=test_source,
            out=summary.predict_dir,
            imgsz=imgsz,
            conf=predict_conf,
        )

    export_onnx(
        weights=summary.weights,
        out=summary.artifact_dir,
        imgsz=imgsz,
        dataset=dataset,
        version=version,
        opset=opset,
        simplify=simplify,
    )

    if install_candidate:
        root = candidate_root.resolve() if candidate_root else default_candidate_root()
        summary.candidate_install_dir = root / version

    _write_report(summary)

    if install_candidate and summary.candidate_install_dir:
        install_candidate_artifact(summary.artifact_dir, summary.candidate_install_dir)

    return summary


def default_artifacts_root() -> Path:
    return tool_root().parents[1] / "artifacts"


def collect_sample_inventory(source: Path, bucket: str = "all") -> SampleInventory:
    source = source.resolve()
    if not source.exists():
        raise VisionTrainerError(f"RPA 学习样本目录不存在：{source}")

    scanned_images = 0
    skipped_duplicates = 0
    skipped_missing_labels = 0
    skipped_empty_labels = 0
    seen_hashes: set[str] = set()
    bucket_counts: dict[str, int] = {}
    samples: list[RpaSample] = []

    for bucket_name, sample_dir in _resolve_sample_directories(source, bucket):
        if not sample_dir.exists():
            continue

        for image_path in sorted(path for path in sample_dir.iterdir() if _is_image(path)):
            scanned_images += 1
            label_path = image_path.with_suffix(".txt")
            if not label_path.exists():
                skipped_missing_labels += 1
                continue

            label_text = label_path.read_text(encoding="utf-8").strip()
            if not label_text:
                skipped_empty_labels += 1
                continue

            image_hash = _file_sha256(image_path)
            if image_hash in seen_hashes:
                skipped_duplicates += 1
                continue

            seen_hashes.add(image_hash)
            metadata_path = image_path.with_suffix(".json")
            sample = RpaSample(
                bucket=bucket_name,
                image_path=image_path,
                label_path=label_path,
                metadata_path=metadata_path if metadata_path.exists() else None,
                image_hash=image_hash,
            )
            samples.append(sample)
            bucket_counts[bucket_name] = bucket_counts.get(bucket_name, 0) + 1

    return SampleInventory(
        scanned_images=scanned_images,
        usable_samples=len(samples),
        skipped_duplicates=skipped_duplicates,
        skipped_missing_labels=skipped_missing_labels,
        skipped_empty_labels=skipped_empty_labels,
        bucket_counts=bucket_counts,
        samples=tuple(samples),
    )


def create_review_pack(
    *,
    samples: tuple[RpaSample, ...],
    out_dir: Path,
    review_count: int,
    seed: int,
    version: str,
    source: Path,
) -> Path:
    out_dir = out_dir.resolve()
    out_dir.mkdir(parents=True, exist_ok=True)
    write_labels_file(out_dir / LABELS_FILE_NAME)
    write_labels_file(out_dir / "classes.txt")

    selected = _select_review_samples(samples, review_count, seed)
    manifest_samples = []
    used_names: set[str] = set()

    for sample in selected:
        target_image = _unique_target_path(out_dir, sample.image_path.name, sample.image_hash, used_names)
        target_label = target_image.with_suffix(".txt")
        target_metadata = target_image.with_suffix(".json")

        shutil.copy2(sample.image_path, target_image)
        shutil.copy2(sample.label_path, target_label)
        if sample.metadata_path:
            shutil.copy2(sample.metadata_path, target_metadata)

        manifest_samples.append(
            {
                "bucket": sample.bucket,
                "image": str(sample.image_path),
                "label": str(sample.label_path),
                "metadata": str(sample.metadata_path) if sample.metadata_path else None,
                "imageHash": sample.image_hash,
                "reviewImage": str(target_image),
                "reviewLabel": str(target_label),
                "reviewMetadata": str(target_metadata) if sample.metadata_path else None,
            }
        )

    manifest_path = out_dir / "review-manifest.json"
    payload = {
        "version": version,
        "generatedAt": _now_iso(),
        "source": str(source.resolve()),
        "requestedReviewCount": review_count,
        "selectedCount": len(selected),
        "selectionPolicy": "优先随机抽取 review，不足时从 accepted 补齐。",
        "samples": manifest_samples,
    }
    manifest_path.write_text(json.dumps(payload, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    return manifest_path


def install_candidate_artifact(artifact_dir: Path, target_dir: Path) -> None:
    artifact_dir = artifact_dir.resolve()
    target_dir = target_dir.resolve()
    missing = [name for name in CANDIDATE_FILES if not (artifact_dir / name).exists()]
    if missing:
        raise VisionTrainerError(f"候选模型产物不完整，缺少：{', '.join(missing)}")

    target_dir.mkdir(parents=True, exist_ok=True)
    for file_name in CANDIDATE_FILES:
        source = (artifact_dir / file_name).resolve()
        target = (target_dir / file_name).resolve()
        if source == target:
            continue
        shutil.copy2(source, target)


def _resolve_sample_directories(source: Path, bucket: str) -> list[tuple[str, Path]]:
    if bucket == "accepted-fixed":
        directories = [(name, source / name) for name in ("accepted", "fixed") if (source / name).is_dir()]
        if directories:
            return directories

        return [(_bucket_name(source), source)]

    if bucket == "all":
        directories = [(name, source / name) for name in ("accepted", "fixed", "review") if (source / name).is_dir()]
        if directories:
            return directories

        return [(_bucket_name(source), source)]

    candidate = source / bucket
    if candidate.is_dir():
        return [(bucket, candidate)]

    return [("source", source)]


def _select_review_samples(samples: tuple[RpaSample, ...], review_count: int, seed: int) -> list[RpaSample]:
    if review_count <= 0:
        return []

    rng = random.Random(seed)
    review_samples = [sample for sample in samples if sample.bucket == "review"]
    accepted_samples = [sample for sample in samples if sample.bucket != "review"]
    rng.shuffle(review_samples)
    rng.shuffle(accepted_samples)

    selected = review_samples[:review_count]
    if len(selected) < review_count:
        selected.extend(accepted_samples[: review_count - len(selected)])

    return selected


def _write_report(summary: ActiveLearnSummary) -> None:
    if summary.report_path is None:
        return

    summary.report_path.parent.mkdir(parents=True, exist_ok=True)
    summary.report_path.write_text(
        json.dumps(_report_payload(summary), ensure_ascii=False, indent=2) + "\n",
        encoding="utf-8",
    )


def _report_payload(summary: ActiveLearnSummary) -> dict[str, object]:
    return {
        "version": summary.version,
        "generatedAt": _now_iso(),
        "source": str(summary.source),
        "dataset": str(summary.dataset),
        "parameters": {
            "minSamples": summary.min_samples,
            "reviewCount": summary.review_count,
            "bucket": summary.bucket,
            "model": summary.model,
            "epochs": summary.epochs,
            "imgsz": summary.imgsz,
            "batch": summary.batch,
            "device": summary.device,
            "valRatio": summary.val_ratio,
            "testRatio": summary.test_ratio,
            "seed": summary.seed,
            "predictConf": summary.predict_conf,
            "dryRun": summary.dry_run,
            "skipTrain": summary.skip_train,
            "autolabelSamplesEnabled": summary.autolabel_samples_enabled,
            "autolabelOverwrite": summary.autolabel_overwrite,
            "autolabelMaxWidth": summary.autolabel_max_width,
        },
        "sampleInventory": _inventory_payload(summary.inventory),
        "autolabelSamples": _autolabel_payload(summary.autolabel_summary),
        "ingest": _ingest_payload(summary.ingest),
        "validationBeforeSplit": _validation_payload(summary.validation_before_split),
        "split": _split_payload(summary.split),
        "validationAfterSplit": _validation_payload(summary.validation_after_split),
        "paths": {
            "reviewDir": _path_text(summary.review_dir),
            "reviewManifest": _path_text(summary.review_manifest),
            "trainDir": _path_text(summary.train_dir),
            "weights": _path_text(summary.weights),
            "predictDir": _path_text(summary.predict_dir),
            "artifactDir": _path_text(summary.artifact_dir),
            "candidateInstallDir": _path_text(summary.candidate_install_dir),
            "report": _path_text(summary.report_path),
        },
    }


def _inventory_payload(inventory: SampleInventory) -> dict[str, object]:
    return {
        "scannedImages": inventory.scanned_images,
        "usableSamples": inventory.usable_samples,
        "skippedDuplicates": inventory.skipped_duplicates,
        "skippedMissingLabels": inventory.skipped_missing_labels,
        "skippedEmptyLabels": inventory.skipped_empty_labels,
        "bucketCounts": inventory.bucket_counts,
    }


def _autolabel_payload(summary: AutolabelSummary | None) -> dict[str, object] | None:
    if summary is None:
        return None

    return {
        "checkedImages": summary.checked_images,
        "updatedImages": summary.updated_images,
        "unchangedImages": summary.unchanged_images,
        "createdLabels": summary.created_labels,
        "addedSendButtons": summary.added_send_buttons,
        "addedCustomerBubbles": summary.added_customer_bubbles,
        "addedSelfBubbles": summary.added_self_bubbles,
        "warnings": summary.warnings,
    }


def _ingest_payload(summary: RpaIngestSummary | None) -> dict[str, int] | None:
    if summary is None:
        return None

    return {
        "scanned": summary.scanned,
        "imported": summary.imported,
        "skippedDuplicates": summary.skipped_duplicates,
        "skippedMissingLabels": summary.skipped_missing_labels,
        "skippedEmptyLabels": summary.skipped_empty_labels,
        "updatedDuplicateLabels": summary.updated_duplicate_labels,
    }


def _validation_payload(result: ValidationResult | None) -> dict[str, object] | None:
    if result is None:
        return None

    return {
        "ok": result.ok,
        "checkedImages": result.checked_images,
        "checkedLabels": result.checked_labels,
        "errors": result.errors,
        "warnings": result.warnings,
    }


def _split_payload(summary: SplitSummary | None) -> dict[str, int] | None:
    if summary is None:
        return None

    return {
        "train": summary.train,
        "val": summary.val,
        "test": summary.test,
    }


def _ensure_validation_ok(stage: str, result: ValidationResult) -> None:
    if result.ok:
        return

    first_errors = "\n".join(f"- {error}" for error in result.errors[:10])
    more = f"\n... 另有 {len(result.errors) - 10} 个错误" if len(result.errors) > 10 else ""
    raise VisionTrainerError(f"{stage}失败，共 {len(result.errors)} 个错误：\n{first_errors}{more}")


def _unique_target_path(out_dir: Path, file_name: str, image_hash: str, used_names: set[str]) -> Path:
    target = out_dir / file_name
    if target.name.lower() not in used_names and not target.exists():
        used_names.add(target.name.lower())
        return target

    original = Path(file_name)
    stem = original.stem
    suffix = original.suffix
    counter = 0
    while True:
        extra = f"-{counter}" if counter else ""
        candidate = out_dir / f"{stem}-{image_hash[:10]}{extra}{suffix}"
        if candidate.name.lower() not in used_names and not candidate.exists():
            used_names.add(candidate.name.lower())
            return candidate
        counter += 1


def _is_image(path: Path) -> bool:
    return path.is_file() and path.suffix.lower() in IMAGE_EXTENSIONS


def _file_sha256(path: Path) -> str:
    hasher = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            hasher.update(chunk)
    return hasher.hexdigest()


def _bucket_name(source: Path) -> str:
    name = source.name.lower()
    return name if name in {"review", "accepted"} else "source"


def _default_version() -> str:
    return "m4.3-active-" + datetime.now().strftime("%Y%m%d-%H%M%S")


def _now_iso() -> str:
    return datetime.now().astimezone().isoformat(timespec="seconds")


def _path_text(path: Path | None) -> str | None:
    return str(path) if path else None
