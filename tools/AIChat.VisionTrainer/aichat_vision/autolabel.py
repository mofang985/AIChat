from __future__ import annotations

from dataclasses import dataclass, field
from pathlib import Path
from typing import Callable

from .dataset import image_files
from .errors import VisionTrainerError
from .prelabel import DEFAULT_BOXES

CHAT_CONTENT_CLASS_ID = 1
INPUT_AREA_CLASS_ID = 2
SEND_BUTTON_CLASS_ID = 4
CUSTOMER_BUBBLE_CLASS_ID = 5
SELF_BUBBLE_CLASS_ID = 6
AUTO_CLASS_IDS = {SEND_BUTTON_CLASS_ID, CUSTOMER_BUBBLE_CLASS_ID, SELF_BUBBLE_CLASS_ID}
SAMPLE_BUCKETS = {"source", "accepted", "review", "fixed", "accepted-fixed", "all"}



@dataclass(frozen=True)
class PixelBox:
    left: float
    top: float
    right: float
    bottom: float

    @property
    def width(self) -> float:
        return max(0.0, self.right - self.left)

    @property
    def height(self) -> float:
        return max(0.0, self.bottom - self.top)

    @property
    def area(self) -> float:
        return self.width * self.height

    @property
    def center_x(self) -> float:
        return self.left + self.width / 2

    @property
    def center_y(self) -> float:
        return self.top + self.height / 2

    def expanded(self, x_margin: float, y_margin: float) -> PixelBox:
        return PixelBox(
            self.left - x_margin,
            self.top - y_margin,
            self.right + x_margin,
            self.bottom + y_margin,
        )

    def clipped(self, width: int, height: int) -> PixelBox:
        return PixelBox(
            max(0.0, min(float(width), self.left)),
            max(0.0, min(float(height), self.top)),
            max(0.0, min(float(width), self.right)),
            max(0.0, min(float(height), self.bottom)),
        )

    def scaled(self, scale: float) -> PixelBox:
        return PixelBox(
            self.left * scale,
            self.top * scale,
            self.right * scale,
            self.bottom * scale,
        )


@dataclass(frozen=True)
class YoloBox:
    class_id: int
    x_center: float
    y_center: float
    width: float
    height: float

    def to_pixel_box(self, image_width: int, image_height: int) -> PixelBox:
        box_width = self.width * image_width
        box_height = self.height * image_height
        center_x = self.x_center * image_width
        center_y = self.y_center * image_height
        return PixelBox(
            center_x - box_width / 2,
            center_y - box_height / 2,
            center_x + box_width / 2,
            center_y + box_height / 2,
        ).clipped(image_width, image_height)


@dataclass(frozen=True)
class Component:
    box: PixelBox
    pixel_count: int
    density: float


@dataclass
class AutolabelSummary:
    checked_images: int = 0
    updated_images: int = 0
    unchanged_images: int = 0
    created_labels: int = 0
    added_send_buttons: int = 0
    added_customer_bubbles: int = 0
    added_self_bubbles: int = 0
    warnings: list[str] = field(default_factory=list)


def autolabel_dataset(
    dataset: Path,
    overwrite: bool = False,
    dry_run: bool = False,
    max_width: int = 1280,
) -> AutolabelSummary:
    dataset = dataset.resolve()
    raw_dir = dataset / "raw"
    if not raw_dir.exists():
        raise VisionTrainerError(f"raw 目录不存在：{raw_dir}")

    return _autolabel_image_directories(
        directories=(raw_dir,),
        overwrite=overwrite,
        dry_run=dry_run,
        max_width=max_width,
        empty_message=f"raw 目录没有找到可自动标注图片：{raw_dir}",
        create_missing_labels=True,
    )


def autolabel_samples(
    source: Path,
    bucket: str = "accepted",
    overwrite: bool = False,
    dry_run: bool = False,
    max_width: int = 1280,
) -> AutolabelSummary:
    if bucket not in SAMPLE_BUCKETS:
        raise VisionTrainerError("bucket 只能是 source、accepted、review、fixed、accepted-fixed 或 all。")

    source = source.resolve()
    if not source.exists():
        raise VisionTrainerError(f"RPA 学习样本目录不存在：{source}")

    directories = _resolve_sample_directories(source, bucket)
    if not directories:
        raise VisionTrainerError(f"没有找到可自动补标的样本分桶：source={source}, bucket={bucket}")

    return _autolabel_image_directories(
        directories=tuple(directories),
        overwrite=overwrite,
        dry_run=dry_run,
        max_width=max_width,
        empty_message=f"样本目录没有找到可自动补标图片：source={source}, bucket={bucket}",
        create_missing_labels=False,
    )


def _autolabel_image_directories(
    directories: tuple[Path, ...],
    overwrite: bool,
    dry_run: bool,
    max_width: int,
    empty_message: str,
    create_missing_labels: bool,
) -> AutolabelSummary:
    if max_width < 480:
        raise VisionTrainerError("--max-width 不能小于 480，否则气泡太小容易误检。")

    try:
        from PIL import Image
    except ModuleNotFoundError as exc:
        raise VisionTrainerError("缺少 Pillow 依赖，请先执行：pip install -r requirements.txt") from exc

    images: list[Path] = []
    for directory in directories:
        images.extend(image_files(directory))

    if not images:
        raise VisionTrainerError(empty_message)

    summary = AutolabelSummary()
    for image_path in images:
        summary.checked_images += 1
        label_path = image_path.with_suffix(".txt")
        if not label_path.exists() and not create_missing_labels:
            summary.unchanged_images += 1
            summary.warnings.append(f"{image_path}: 缺少同名 .txt 标签，已跳过自动补标。")
            continue


        try:
            with Image.open(image_path) as image:
                image = image.convert("RGB")
                image_width, image_height = image.size
                existing_boxes = _read_yolo_boxes(label_path)
                base_boxes = _ensure_reference_boxes(existing_boxes)
                candidates = _detect_candidates(image, base_boxes, max_width)
        except (OSError, VisionTrainerError) as exc:
            summary.unchanged_images += 1
            summary.warnings.append(f"{image_path}: {exc}")
            continue

        if overwrite:
            next_boxes = [box for box in base_boxes if box.class_id not in AUTO_CLASS_IDS]
            removed_auto_boxes = len(base_boxes) - len(next_boxes)
        else:
            next_boxes = list(base_boxes)
            removed_auto_boxes = 0

        added = _append_non_overlapping(next_boxes, candidates, image_width, image_height)
        changed = bool(added) or (overwrite and removed_auto_boxes > 0)
        if not changed:
            summary.unchanged_images += 1
            continue

        if not label_path.exists():
            summary.created_labels += 1

        summary.updated_images += 1
        summary.added_send_buttons += sum(1 for box in added if box.class_id == SEND_BUTTON_CLASS_ID)
        summary.added_customer_bubbles += sum(1 for box in added if box.class_id == CUSTOMER_BUBBLE_CLASS_ID)
        summary.added_self_bubbles += sum(1 for box in added if box.class_id == SELF_BUBBLE_CLASS_ID)

        if not dry_run:
            label_path.write_text(_format_yolo_boxes(next_boxes), encoding="utf-8")

    return summary


def _resolve_sample_directories(source: Path, bucket: str) -> list[Path]:
    if bucket == "source":
        return [source]

    if bucket == "accepted-fixed":
        directories = [source / name for name in ("accepted", "fixed")]
        existing = [directory for directory in directories if directory.is_dir()]
        return existing or [source]

    if bucket == "all":
        directories = [source / name for name in ("accepted", "fixed", "review")]
        existing = [directory for directory in directories if directory.is_dir()]
        return existing or [source]

    if source.name.lower() == bucket:
        return [source]

    candidate = source / bucket
    return [candidate] if candidate.is_dir() else []



def _detect_candidates(image, boxes: list[YoloBox], max_width: int) -> list[YoloBox]:
    image_width, image_height = image.size
    scale = min(1.0, max_width / image_width)
    scaled_size = (round(image_width * scale), round(image_height * scale))
    scaled_image = image if scale == 1.0 else image.resize(scaled_size, _resampling_box())

    chat_box = _reference_pixel_box(boxes, CHAT_CONTENT_CLASS_ID, image_width, image_height)
    input_area_box = _reference_pixel_box(boxes, INPUT_AREA_CLASS_ID, image_width, image_height)

    candidates: list[YoloBox] = []
    candidates.extend(
        _detect_bubbles(
            scaled_image=scaled_image,
            original_region=chat_box,
            scale=scale,
            image_width=image_width,
            image_height=image_height,
            class_id=CUSTOMER_BUBBLE_CLASS_ID,
            predicate=_is_customer_bubble_pixel,
            side="left",
        )
    )
    candidates.extend(
        _detect_bubbles(
            scaled_image=scaled_image,
            original_region=chat_box,
            scale=scale,
            image_width=image_width,
            image_height=image_height,
            class_id=SELF_BUBBLE_CLASS_ID,
            predicate=_is_self_bubble_pixel,
            side="right",
        )
    )
    candidates.append(_detect_send_button(scaled_image, input_area_box, scale, image_width, image_height))
    return candidates


def _detect_bubbles(
    scaled_image,
    original_region: PixelBox,
    scale: float,
    image_width: int,
    image_height: int,
    class_id: int,
    predicate: Callable[[int, int, int], bool],
    side: str,
) -> list[YoloBox]:
    scaled_region = _integer_box(original_region.scaled(scale), scaled_image.width, scaled_image.height)
    components = _find_components(scaled_image, scaled_region, predicate)
    detected: list[PixelBox] = []

    for component in components:
        original_box = _scale_from_detection(component.box, scale).clipped(image_width, image_height)
        if not _is_probable_bubble(original_box, component.density, original_region, image_width, image_height):
            continue

        if side == "left" and original_box.center_x > original_region.left + original_region.width * 0.58:
            continue
        if side == "right" and original_box.center_x < original_region.left + original_region.width * 0.42:
            continue

        detected.append(_expand_detected_bubble(original_box, image_width, image_height))

    merged = _merge_nearby_same_line_boxes(detected)
    return [_pixel_to_yolo_box(class_id, box, image_width, image_height) for box in merged]


def _detect_send_button(
    scaled_image,
    input_area_box: PixelBox,
    scale: float,
    image_width: int,
    image_height: int,
) -> YoloBox:
    search_box = PixelBox(
        input_area_box.left + input_area_box.width * 0.72,
        input_area_box.top + input_area_box.height * 0.45,
        input_area_box.right,
        input_area_box.bottom,
    )
    scaled_search_box = _integer_box(search_box.scaled(scale), scaled_image.width, scaled_image.height)
    components = _find_components(scaled_image, scaled_search_box, _is_enabled_send_button_pixel)

    candidates: list[PixelBox] = []
    for component in components:
        original_box = _scale_from_detection(component.box, scale).clipped(image_width, image_height)
        if component.density < 0.35:
            continue
        if original_box.width < 45 or original_box.height < 22:
            continue
        if original_box.width > 220 or original_box.height > 90:
            continue
        candidates.append(original_box.expanded(4, 3).clipped(image_width, image_height))

    if candidates:
        selected = max(candidates, key=lambda box: (box.center_x, box.center_y, box.area))
        return _pixel_to_yolo_box(SEND_BUTTON_CLASS_ID, selected, image_width, image_height)

    # 空输入框时按钮是浅灰色，和输入区背景很接近；此时用输入区右下角稳定相对位置兜底。
    button_width = min(max(input_area_box.width * 0.030, 60), 140)
    button_height = min(max(input_area_box.height * 0.115, 28), 64)
    right = input_area_box.right - max(input_area_box.width * 0.004, 12)
    bottom = input_area_box.bottom - max(input_area_box.height * 0.020, 8)
    fallback = PixelBox(
        right - button_width,
        bottom - button_height,
        right,
        bottom,
    ).clipped(image_width, image_height)
    return _pixel_to_yolo_box(SEND_BUTTON_CLASS_ID, fallback, image_width, image_height)


def _find_components(
    image,
    region: tuple[int, int, int, int],
    predicate: Callable[[int, int, int], bool],
) -> list[Component]:
    left, top, right, bottom = region
    if right <= left or bottom <= top:
        return []

    crop = image.crop((left, top, right, bottom))
    pixels = crop.load()
    width, height = crop.size
    mask = bytearray(width * height)
    for y in range(height):
        row_offset = y * width
        for x in range(width):
            if predicate(*pixels[x, y]):
                mask[row_offset + x] = 1

    components: list[Component] = []
    for index, value in enumerate(mask):
        if not value:
            continue

        stack = [index]
        mask[index] = 0
        min_x = max_x = index % width
        min_y = max_y = index // width
        pixel_count = 0

        while stack:
            current = stack.pop()
            x = current % width
            y = current // width
            pixel_count += 1
            min_x = min(min_x, x)
            max_x = max(max_x, x)
            min_y = min(min_y, y)
            max_y = max(max_y, y)

            if x > 0:
                neighbor = current - 1
                if mask[neighbor]:
                    mask[neighbor] = 0
                    stack.append(neighbor)
            if x + 1 < width:
                neighbor = current + 1
                if mask[neighbor]:
                    mask[neighbor] = 0
                    stack.append(neighbor)
            if y > 0:
                neighbor = current - width
                if mask[neighbor]:
                    mask[neighbor] = 0
                    stack.append(neighbor)
            if y + 1 < height:
                neighbor = current + width
                if mask[neighbor]:
                    mask[neighbor] = 0
                    stack.append(neighbor)

        box = PixelBox(left + min_x, top + min_y, left + max_x + 1, top + max_y + 1)
        density = pixel_count / max(1.0, box.area)
        components.append(Component(box=box, pixel_count=pixel_count, density=density))

    return components


def _is_customer_bubble_pixel(red: int, green: int, blue: int) -> bool:
    return (
        226 <= red <= 246
        and 226 <= green <= 246
        and 226 <= blue <= 250
        and max(red, green, blue) - min(red, green, blue) <= 14
    )


def _is_self_bubble_pixel(red: int, green: int, blue: int) -> bool:
    return (
        120 <= red <= 195
        and green >= 185
        and 80 <= blue <= 185
        and green - red >= 35
        and green - blue >= 35
    )


def _is_enabled_send_button_pixel(red: int, green: int, blue: int) -> bool:
    return (
        green >= 145
        and green - red >= 45
        and green - blue >= 25
        and red <= 170
        and blue <= 170
    )


def _is_probable_bubble(
    box: PixelBox,
    density: float,
    chat_region: PixelBox,
    image_width: int,
    image_height: int,
) -> bool:
    min_width = max(35, image_width * 0.008)
    min_height = max(16, image_height * 0.007)
    if box.width < min_width or box.height < min_height:
        return False
    if box.width > chat_region.width * 0.55:
        return False
    if box.height > chat_region.height * 0.22:
        return False
    if density < 0.40:
        return False
    return True


def _expand_detected_bubble(box: PixelBox, image_width: int, image_height: int) -> PixelBox:
    x_margin = max(3, box.width * 0.015)
    y_margin = max(2, box.height * 0.060)
    return box.expanded(x_margin, y_margin).clipped(image_width, image_height)


def _merge_nearby_same_line_boxes(boxes: list[PixelBox]) -> list[PixelBox]:
    if not boxes:
        return []

    merged: list[PixelBox] = []
    for box in sorted(boxes, key=lambda item: (item.top, item.left)):
        for index, existing in enumerate(merged):
            vertical_overlap = min(existing.bottom, box.bottom) - max(existing.top, box.top)
            min_height = max(1.0, min(existing.height, box.height))
            horizontal_gap = max(existing.left, box.left) - min(existing.right, box.right)
            if vertical_overlap / min_height >= 0.70 and horizontal_gap <= 12:
                merged[index] = PixelBox(
                    min(existing.left, box.left),
                    min(existing.top, box.top),
                    max(existing.right, box.right),
                    max(existing.bottom, box.bottom),
                )
                break
        else:
            merged.append(box)

    return sorted(merged, key=lambda item: (item.top, item.left))


def _append_non_overlapping(
    target_boxes: list[YoloBox],
    candidates: list[YoloBox],
    image_width: int,
    image_height: int,
) -> list[YoloBox]:
    added: list[YoloBox] = []
    for candidate in candidates:
        if candidate.class_id not in AUTO_CLASS_IDS:
            continue
        if any(
            existing.class_id == candidate.class_id
            and _iou(
                existing.to_pixel_box(image_width, image_height),
                candidate.to_pixel_box(image_width, image_height),
            )
            >= 0.35
            for existing in target_boxes
        ):
            continue

        target_boxes.append(candidate)
        added.append(candidate)

    target_boxes.sort(key=lambda box: (box.class_id, box.y_center, box.x_center))
    return added


def _read_yolo_boxes(path: Path) -> list[YoloBox]:
    if not path.exists():
        return []

    boxes: list[YoloBox] = []
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        stripped = line.strip()
        if not stripped:
            continue
        parts = stripped.split()
        if len(parts) != 5:
            continue
        try:
            class_id = int(parts[0])
            x_center, y_center, width, height = [float(value) for value in parts[1:]]
        except ValueError:
            continue

        boxes.append(YoloBox(class_id, x_center, y_center, width, height))
    return boxes


def _ensure_reference_boxes(boxes: list[YoloBox]) -> list[YoloBox]:
    next_boxes = list(boxes)
    existing_class_ids = {box.class_id for box in next_boxes}
    for default in DEFAULT_BOXES:
        class_id, x_center, y_center, width, height = default
        if class_id not in existing_class_ids:
            next_boxes.append(YoloBox(class_id, x_center, y_center, width, height))
    return next_boxes


def _reference_pixel_box(boxes: list[YoloBox], class_id: int, image_width: int, image_height: int) -> PixelBox:
    for box in boxes:
        if box.class_id == class_id:
            return box.to_pixel_box(image_width, image_height)

    for default in DEFAULT_BOXES:
        default_class_id, x_center, y_center, width, height = default
        if default_class_id == class_id:
            return YoloBox(default_class_id, x_center, y_center, width, height).to_pixel_box(image_width, image_height)

    raise VisionTrainerError(f"缺少自动标注所需的大区域 class_id={class_id}")


def _pixel_to_yolo_box(class_id: int, box: PixelBox, image_width: int, image_height: int) -> YoloBox:
    clipped = box.clipped(image_width, image_height)
    return YoloBox(
        class_id=class_id,
        x_center=_clamp(clipped.center_x / image_width),
        y_center=_clamp(clipped.center_y / image_height),
        width=_clamp(clipped.width / image_width),
        height=_clamp(clipped.height / image_height),
    )


def _format_yolo_boxes(boxes: list[YoloBox]) -> str:
    return "".join(
        f"{box.class_id} {box.x_center:.6f} {box.y_center:.6f} {box.width:.6f} {box.height:.6f}\n"
        for box in boxes
    )


def _scale_from_detection(box: PixelBox, scale: float) -> PixelBox:
    if scale == 1.0:
        return box
    return PixelBox(
        box.left / scale,
        box.top / scale,
        box.right / scale,
        box.bottom / scale,
    )


def _integer_box(box: PixelBox, image_width: int, image_height: int) -> tuple[int, int, int, int]:
    clipped = box.clipped(image_width, image_height)
    return (
        int(clipped.left),
        int(clipped.top),
        int(clipped.right),
        int(clipped.bottom),
    )


def _iou(first: PixelBox, second: PixelBox) -> float:
    intersection_left = max(first.left, second.left)
    intersection_top = max(first.top, second.top)
    intersection_right = min(first.right, second.right)
    intersection_bottom = min(first.bottom, second.bottom)
    intersection_width = max(0.0, intersection_right - intersection_left)
    intersection_height = max(0.0, intersection_bottom - intersection_top)
    intersection_area = intersection_width * intersection_height
    union_area = first.area + second.area - intersection_area
    if union_area <= 0:
        return 0.0
    return intersection_area / union_area


def _clamp(value: float) -> float:
    return max(0.0, min(1.0, value))


def _resampling_box():
    from PIL import Image

    return Image.Resampling.BOX
