from __future__ import annotations

import shutil
import time
from dataclasses import dataclass
from datetime import datetime
from pathlib import Path

from .dataset import IMAGE_EXTENSIONS
from .dpi import enable_dpi_awareness
from .errors import VisionTrainerError


@dataclass(frozen=True)
class WindowInfo:
    index: int
    hwnd: int
    title: str
    client_rect: tuple[int, int, int, int]
    window_rect: tuple[int, int, int, int]


@dataclass(frozen=True)
class MonitorInfo:
    index: int
    device: str
    monitor_rect: tuple[int, int, int, int]
    work_rect: tuple[int, int, int, int]
    primary: bool


def import_images_from_directory(source_dir: Path, out_dir: Path) -> int:
    source_dir = source_dir.resolve()
    out_dir = out_dir.resolve()
    if not source_dir.exists():
        raise VisionTrainerError(f"截图来源目录不存在：{source_dir}")

    out_dir.mkdir(parents=True, exist_ok=True)
    copied = 0
    for image_path in sorted(source_dir.rglob("*")):
        if not image_path.is_file() or image_path.suffix.lower() not in IMAGE_EXTENSIONS:
            continue

        target = _unique_target(out_dir / image_path.name)
        shutil.copy2(image_path, target)
        label_path = image_path.with_suffix(".txt")
        if label_path.exists():
            shutil.copy2(label_path, target.with_suffix(".txt"))
        copied += 1

    if copied == 0:
        raise VisionTrainerError(f"来源目录没有找到可导入图片：{source_dir}")

    return copied


def list_wechat_windows(title_keyword: str = "微信") -> list[WindowInfo]:
    enable_dpi_awareness()
    try:
        import win32gui
    except ModuleNotFoundError as exc:
        raise VisionTrainerError("缺少窗口截图依赖，请先执行：pip install -r requirements.txt") from exc

    return _matching_windows(win32gui, title_keyword)


def list_monitors() -> list[MonitorInfo]:
    enable_dpi_awareness()
    try:
        import win32api
    except ModuleNotFoundError as exc:
        raise VisionTrainerError("缺少窗口截图依赖，请先执行：pip install -r requirements.txt") from exc

    monitors: list[MonitorInfo] = []
    for index, (handle, _hdc, _rect) in enumerate(win32api.EnumDisplayMonitors()):
        info = win32api.GetMonitorInfo(handle)
        flags = int(info.get("Flags", 0))
        monitors.append(
            MonitorInfo(
                index=index,
                device=str(info.get("Device", "")),
                monitor_rect=tuple(info["Monitor"]),
                work_rect=tuple(info["Work"]),
                primary=bool(flags & 1),
            )
        )

    return monitors


def capture_wechat_window(
    out_dir: Path,
    title_keyword: str = "微信",
    mode: str = "client",
    window_index: int = 0,
) -> Path:
    enable_dpi_awareness()
    try:
        from PIL import ImageGrab
        import win32gui
    except ModuleNotFoundError as exc:
        raise VisionTrainerError("缺少窗口截图依赖，请先执行：pip install -r requirements.txt") from exc

    hwnd: int | None = None
    if mode != "screen":
        windows = _matching_windows(win32gui, title_keyword)
        if not windows:
            raise VisionTrainerError(f"未找到标题包含“{title_keyword}”的可见微信窗口。")
        if window_index < 0 or window_index >= len(windows):
            raise VisionTrainerError(f"window-index 越界：{window_index}。请先执行 windows 命令查看可用窗口。")

        hwnd = windows[window_index].hwnd

    out_dir.mkdir(parents=True, exist_ok=True)

    if mode == "screen":
        image = ImageGrab.grab(all_screens=True)
        width, height = image.size
    else:
        assert hwnd is not None
        bbox = _capture_bbox(win32gui, hwnd, mode)
        left, top, right, bottom = bbox
        width = right - left
        height = bottom - top
        if width <= 0 or height <= 0:
            raise VisionTrainerError("微信窗口截图区域尺寸无效，请确认窗口未最小化。")

        image = ImageGrab.grab(bbox=bbox, all_screens=True)

    timestamp = datetime.now().strftime("%Y%m%d_%H%M%S")
    file_name = f"wechat_{timestamp}_{mode}_{width}x{height}.png"
    target = _unique_target(out_dir / file_name)
    image.save(target)
    return target


def capture_wechat_windows(
    out_dir: Path,
    title_keyword: str = "微信",
    count: int = 1,
    interval: float = 2.0,
    mode: str = "client",
    window_index: int = 0,
) -> list[Path]:
    if count <= 0:
        raise VisionTrainerError("count 必须大于 0。")
    if interval < 0:
        raise VisionTrainerError("interval 必须大于等于 0。")

    captured: list[Path] = []
    for index in range(count):
        captured.append(capture_wechat_window(out_dir, title_keyword, mode, window_index))
        if index < count - 1 and interval > 0:
            time.sleep(interval)

    return captured


def _matching_windows(win32gui_module, title_keyword: str) -> list[WindowInfo]:
    matches: list[tuple[int, str]] = []

    def enum_handler(hwnd: int, _: object) -> None:
        if not win32gui_module.IsWindowVisible(hwnd) or win32gui_module.IsIconic(hwnd):
            return

        title = win32gui_module.GetWindowText(hwnd).strip()
        if title_keyword in title:
            matches.append((hwnd, title))

    win32gui_module.EnumWindows(enum_handler, None)
    return [
        WindowInfo(
            index=index,
            hwnd=hwnd,
            title=title,
            client_rect=_client_bbox(win32gui_module, hwnd),
            window_rect=win32gui_module.GetWindowRect(hwnd),
        )
        for index, (hwnd, title) in enumerate(matches)
    ]


def _capture_bbox(win32gui_module, hwnd: int, mode: str) -> tuple[int, int, int, int]:
    if mode == "client":
        return _client_bbox(win32gui_module, hwnd)
    if mode == "window":
        return win32gui_module.GetWindowRect(hwnd)

    raise VisionTrainerError(f"不支持的截图模式：{mode}")


def _client_bbox(win32gui_module, hwnd: int) -> tuple[int, int, int, int]:
    client_left, client_top, client_right, client_bottom = win32gui_module.GetClientRect(hwnd)
    screen_left, screen_top = win32gui_module.ClientToScreen(hwnd, (client_left, client_top))
    screen_right, screen_bottom = win32gui_module.ClientToScreen(hwnd, (client_right, client_bottom))
    return screen_left, screen_top, screen_right, screen_bottom


def _unique_target(target: Path) -> Path:
    if not target.exists():
        return target

    index = 1
    while True:
        candidate = target.with_name(f"{target.stem}_{index:03d}{target.suffix}")
        if not candidate.exists():
            return candidate
        index += 1
