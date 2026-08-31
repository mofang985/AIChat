from __future__ import annotations

import argparse
import json
import os
import queue
import shutil
import subprocess
import sys
import threading
from dataclasses import asdict, dataclass
from datetime import datetime
from pathlib import Path
from typing import Callable

import tkinter as tk
from tkinter import filedialog, messagebox, ttk

from PIL import Image, ImageTk

from .active_learn import collect_sample_inventory, default_artifacts_root
from .autolabel import autolabel_samples
from .dataset import IMAGE_EXTENSIONS, dataset_summary, image_files
from .errors import VisionTrainerError
from .labels import LABELS, LABELS_FILE_NAME, write_labels_file
from .package import PACKAGE_FILES, default_candidate_root, default_local_install_dir
from .paths import tool_root
from .promote import promote_candidate
from .validate import validate_dataset
from .yolo_autolabel import yolo_autolabel_samples

CONFIG_PATH = tool_root() / "config" / "gui-settings.json"
AUTO_CLASS_IDS = {4, 5, 6}
BOX_COLORS = (
    "#00bcd4",
    "#8bc34a",
    "#9575cd",
    "#3f51b5",
    "#2196f3",
    "#4caf50",
    "#ef5350",
)


@dataclass
class GuiSettings:
    source: str
    dataset: str
    artifacts_root: str
    candidate_root: str
    model: str = "yolo11n.pt"
    min_samples: int = 1000
    review_count: int = 50
    bucket: str = "accepted"
    yolo_autolabel_epochs: int = 30
    yolo_autolabel_conf: float = 0.35
    epochs: int = 80
    imgsz: int = 960
    batch: int = 8
    device: str = "0"
    predict_conf: float = 0.15
    autolabel_overwrite: bool = True


@dataclass
class AnnotationBox:
    class_id: int
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

    def normalized_line(self, image_width: int, image_height: int) -> str:
        left = _clamp(self.left, 0.0, float(image_width))
        top = _clamp(self.top, 0.0, float(image_height))
        right = _clamp(self.right, 0.0, float(image_width))
        bottom = _clamp(self.bottom, 0.0, float(image_height))
        if right < left:
            left, right = right, left
        if bottom < top:
            top, bottom = bottom, top

        x_center = ((left + right) / 2) / image_width
        y_center = ((top + bottom) / 2) / image_height
        width = max(1.0, right - left) / image_width
        height = max(1.0, bottom - top) / image_height
        return f"{self.class_id} {x_center:.6f} {y_center:.6f} {width:.6f} {height:.6f}"

    @classmethod
    def from_yolo_line(cls, line: str, image_width: int, image_height: int) -> AnnotationBox | None:
        parts = line.split()
        if len(parts) != 5:
            return None
        try:
            class_id = int(parts[0])
            x_center, y_center, width, height = [float(value) for value in parts[1:]]
        except ValueError:
            return None

        if class_id < 0 or class_id >= len(LABELS) or width <= 0 or height <= 0:
            return None

        pixel_width = width * image_width
        pixel_height = height * image_height
        center_x = x_center * image_width
        center_y = y_center * image_height
        left = center_x - pixel_width / 2
        top = center_y - pixel_height / 2
        right = center_x + pixel_width / 2
        bottom = center_y + pixel_height / 2
        return cls(
            class_id=class_id,
            left=_clamp(left, 0.0, float(image_width)),
            top=_clamp(top, 0.0, float(image_height)),
            right=_clamp(right, 0.0, float(image_width)),
            bottom=_clamp(bottom, 0.0, float(image_height)),
        )


def main(argv: list[str] | None = None) -> int:
    parser = argparse.ArgumentParser(prog="python -m aichat_vision.gui")
    parser.add_argument("--smoke-test", action="store_true", help="只验证 GUI 模块可导入和默认配置可解析，不启动窗口。")
    args = parser.parse_args(argv)
    if args.smoke_test:
        settings = load_settings()
        print(f"GUI 自检通过：source={settings.source}")
        return 0

    app = VisionTrainerApp()
    app.mainloop()
    return 0


class VisionTrainerApp(tk.Tk):
    def __init__(self) -> None:
        super().__init__()
        self.title("AIChat Vision Trainer")
        self.geometry("1380x860")
        self.minsize(1180, 720)
        self.settings = load_settings()
        self.log_queue: queue.Queue[tuple[str, object]] = queue.Queue()
        self.running_process: subprocess.Popen[str] | None = None
        self.running_title = ""
        self.busy = False

        self.source_var = tk.StringVar(value=self.settings.source)
        self.dataset_var = tk.StringVar(value=self.settings.dataset)
        self.artifacts_var = tk.StringVar(value=self.settings.artifacts_root)
        self.candidate_var = tk.StringVar(value=self.settings.candidate_root)

        self._build_layout()
        self.protocol("WM_DELETE_WINDOW", self._on_close)
        self.after(100, self._drain_log_queue)

    def _build_layout(self) -> None:
        self.columnconfigure(0, weight=1)
        self.rowconfigure(0, weight=1)
        self.rowconfigure(1, weight=0)

        self.notebook = ttk.Notebook(self)
        self.notebook.grid(row=0, column=0, sticky="nsew")

        self.status_tab = StatusTab(self.notebook, self)
        self.editor_tab = AnnotationEditorTab(self.notebook, self)
        self.training_tab = TrainingTab(self.notebook, self)
        self.model_tab = ModelTab(self.notebook, self)
        self.settings_tab = SettingsTab(self.notebook, self)

        self.notebook.add(self.status_tab, text="状态总览")
        self.notebook.add(self.editor_tab, text="样本标注")
        self.notebook.add(self.training_tab, text="主动学习训练")
        self.notebook.add(self.model_tab, text="模型管理")
        self.notebook.add(self.settings_tab, text="设置")

        log_frame = ttk.LabelFrame(self, text="运行日志")
        log_frame.grid(row=1, column=0, sticky="ew", padx=8, pady=(0, 8))
        log_frame.columnconfigure(0, weight=1)
        self.log_text = tk.Text(log_frame, height=8, wrap="word")
        self.log_text.grid(row=0, column=0, sticky="ew")
        scroll = ttk.Scrollbar(log_frame, orient="vertical", command=self.log_text.yview)
        scroll.grid(row=0, column=1, sticky="ns")
        self.log_text.configure(yscrollcommand=scroll.set)

        button_frame = ttk.Frame(log_frame)
        button_frame.grid(row=0, column=2, sticky="ns", padx=(6, 0))
        ttk.Button(button_frame, text="清空日志", command=self.clear_log).pack(fill="x", pady=2)
        ttk.Button(button_frame, text="停止任务", command=self.stop_process).pack(fill="x", pady=2)

    def run_command(self, title: str, args: list[str], *, confirm: str | None = None) -> None:
        if self.busy:
            messagebox.showwarning("任务正在运行", f"当前任务未结束：{self.running_title}")
            return
        if confirm and not messagebox.askyesno("确认操作", confirm):
            return

        command = [sys.executable, "-m", "aichat_vision", *args]
        self.append_log(f"\n[{_now_text()}] 开始：{title}")
        self.append_log("命令：" + _command_text(command))
        env = os.environ.copy()
        env["PYTHONIOENCODING"] = "utf-8"
        self.running_title = title
        self.busy = True

        def worker() -> None:
            try:
                process = subprocess.Popen(
                    command,
                    cwd=tool_root(),
                    env=env,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    text=True,
                    encoding="utf-8",
                    errors="replace",
                )
                self.log_queue.put(("process", process))
                assert process.stdout is not None
                for line in process.stdout:
                    self.log_queue.put(("line", line.rstrip("\n")))
                code = process.wait()
                self.log_queue.put(("done", code))
            except Exception as exc:  # noqa: BLE001
                self.log_queue.put(("error", exc))

        threading.Thread(target=worker, daemon=True).start()

    def run_background(self, title: str, action: Callable[[], str], on_success: Callable[[], None] | None = None) -> None:
        if self.busy:
            messagebox.showwarning("任务正在运行", f"当前任务未结束：{self.running_title}")
            return
        self.append_log(f"\n[{_now_text()}] 开始：{title}")

        def worker() -> None:
            try:
                output = action()
                self.log_queue.put(("line", output))
                self.log_queue.put(("callback", on_success))
                self.log_queue.put(("done", 0))
            except Exception as exc:  # noqa: BLE001
                self.log_queue.put(("error", exc))

        self.running_title = title
        self.busy = True
        threading.Thread(target=worker, daemon=True).start()

    def stop_process(self) -> None:
        if self.running_process is None:
            self.append_log("当前没有可停止的子进程任务。")
            return
        if not messagebox.askyesno("确认停止", f"确认停止当前任务：{self.running_title}？"):
            return
        self.running_process.terminate()
        self.append_log("已请求停止当前任务。")

    def append_log(self, text: str) -> None:
        self.log_text.insert("end", text + "\n")
        self.log_text.see("end")

    def clear_log(self) -> None:
        self.log_text.delete("1.0", "end")

    def save_settings(self) -> None:
        self.settings.source = self.source_var.get().strip()
        self.settings.dataset = self.dataset_var.get().strip()
        self.settings.artifacts_root = self.artifacts_var.get().strip()
        self.settings.candidate_root = self.candidate_var.get().strip()
        if hasattr(self, "training_tab"):
            self.settings.bucket = self.training_tab.bucket_var.get()
            self.settings.min_samples = int(self.training_tab.min_samples_var.get())
            self.settings.review_count = int(self.training_tab.review_count_var.get())
            self.settings.model = self.training_tab.model_var.get().strip()
            self.settings.epochs = int(self.training_tab.epochs_var.get())
            self.settings.imgsz = int(self.training_tab.imgsz_var.get())
            self.settings.batch = int(self.training_tab.batch_var.get())
            self.settings.device = self.training_tab.device_var.get().strip()
            self.settings.predict_conf = float(self.training_tab.predict_conf_var.get())
            self.settings.yolo_autolabel_epochs = int(self.editor_tab.yolo_autolabel_epochs_var.get())
            self.settings.yolo_autolabel_conf = float(self.editor_tab.yolo_autolabel_conf_var.get())
            self.settings.autolabel_overwrite = bool(self.training_tab.autolabel_overwrite_var.get())
        save_settings(self.settings)
        self.append_log("设置已保存。")

    def _drain_log_queue(self) -> None:
        try:
            while True:
                kind, payload = self.log_queue.get_nowait()
                if kind == "process":
                    self.running_process = payload  # type: ignore[assignment]
                elif kind == "line":
                    self.append_log(str(payload))
                elif kind == "callback" and payload:
                    payload()  # type: ignore[operator]
                elif kind == "done":
                    self.append_log(f"[{_now_text()}] 结束：{self.running_title}，退出码={payload}")
                    self.running_process = None
                    self.running_title = ""
                    self.busy = False
                    self.status_tab.refresh()
                    self.model_tab.refresh()
                elif kind == "error":
                    self.append_log(f"错误：{payload}")
                    messagebox.showerror("任务失败", str(payload))
                    self.running_process = None
                    self.running_title = ""
                    self.busy = False
        except queue.Empty:
            pass
        self.after(100, self._drain_log_queue)

    def _on_close(self) -> None:
        if self.busy:
            if not messagebox.askyesno("任务仍在运行", "当前有任务仍在运行，确认退出？"):
                return
            if self.running_process is not None:
                self.running_process.terminate()
        self.destroy()


class StatusTab(ttk.Frame):
    def __init__(self, parent: ttk.Notebook, app: VisionTrainerApp) -> None:
        super().__init__(parent)
        self.app = app
        self.columnconfigure(0, weight=1)
        self.rowconfigure(1, weight=1)

        actions = ttk.Frame(self)
        actions.grid(row=0, column=0, sticky="ew", padx=8, pady=8)
        ttk.Button(actions, text="刷新状态", command=self.refresh).pack(side="left", padx=3)
        ttk.Button(actions, text="打开 accepted", command=lambda: open_path(_source_bucket_path(self.app.source_var.get(), "accepted"))).pack(side="left", padx=3)
        ttk.Button(actions, text="打开 review", command=lambda: open_path(_source_bucket_path(self.app.source_var.get(), "review"))).pack(side="left", padx=3)
        ttk.Button(actions, text="打开 rejected", command=lambda: open_path(Path(self.app.source_var.get()) / "rejected")).pack(side="left", padx=3)
        ttk.Button(actions, text="打开数据集", command=lambda: open_path(Path(self.app.dataset_var.get()))).pack(side="left", padx=3)
        ttk.Button(actions, text="打开候选模型", command=lambda: open_path(Path(self.app.candidate_var.get()))).pack(side="left", padx=3)

        self.text = tk.Text(self, wrap="word")
        self.text.grid(row=1, column=0, sticky="nsew", padx=8, pady=(0, 8))
        scroll = ttk.Scrollbar(self, orient="vertical", command=self.text.yview)
        scroll.grid(row=1, column=1, sticky="ns", pady=(0, 8))
        self.text.configure(yscrollcommand=scroll.set)
        self.refresh()

    def refresh(self) -> None:
        source = Path(self.app.source_var.get())
        dataset = Path(self.app.dataset_var.get())
        candidate_root = Path(self.app.candidate_var.get())
        lines = ["路径", f"  学习样本：{source}", f"  数据集：{dataset}", f"  候选模型：{candidate_root}", f"  正式模型：{default_local_install_dir()}", ""]

        lines.append("学习样本")
        for bucket in ("accepted", "fixed", "review"):
            bucket_dir = _source_bucket_path(source, bucket)
            stats = _label_stats(bucket_dir)
            lines.extend(_format_sample_stats(bucket, bucket_dir, stats))
        rejected_dir = source / "rejected"
        rejected_images = len(image_files(rejected_dir)) if rejected_dir.exists() else 0
        lines.append(f"  rejected：图片={rejected_images}")

        try:
            inventory = collect_sample_inventory(source, "accepted")
            lines.append(f"  accepted 可用={inventory.usable_samples}，扫描={inventory.scanned_images}，重复={inventory.skipped_duplicates}，缺标签={inventory.skipped_missing_labels}，空标签={inventory.skipped_empty_labels}")
        except Exception as exc:  # noqa: BLE001
            lines.append(f"  accepted 可用统计失败：{exc}")

        lines.append("")
        lines.append("数据集")
        try:
            summary = dataset_summary(dataset)
            lines.append(f"  train={summary['trainImages']}，val={summary['valImages']}，test={summary['testImages']}")
            result = validate_dataset(dataset)
            lines.append(f"  校验：图片={result.checked_images}，标签={result.checked_labels}，错误={len(result.errors)}，警告={len(result.warnings)}")
            for item in result.errors[:20]:
                lines.append(f"  错误：{item}")
            for item in result.warnings[:20]:
                lines.append(f"  警告：{item}")
            if len(result.warnings) > 20:
                lines.append(f"  另有 {len(result.warnings) - 20} 条警告。")
        except Exception as exc:  # noqa: BLE001
            lines.append(f"  数据集状态读取失败：{exc}")

        lines.append("")
        lines.append("候选模型")
        candidates = _candidate_dirs(candidate_root)
        lines.append(f"  候选数量={len(candidates)}")
        for path in candidates[:10]:
            lines.append(f"  {path.name}")

        self.text.delete("1.0", "end")
        self.text.insert("1.0", "\n".join(lines))


class AnnotationEditorTab(ttk.Frame):
    def __init__(self, parent: ttk.Notebook, app: VisionTrainerApp) -> None:
        super().__init__(parent)
        self.app = app
        self.images: list[Path] = []
        self.index = -1
        self.image: Image.Image | None = None
        self.photo: ImageTk.PhotoImage | None = None
        self.image_size = (0, 0)
        self.boxes: list[AnnotationBox] = []
        self.selected_index: int | None = None
        self.dirty = False
        self.scale = 1.0
        self.offset_x = 0.0
        self.offset_y = 0.0
        self.drag_mode: str | None = None
        self.drag_start: tuple[float, float] | None = None
        self.drag_box: AnnotationBox | None = None
        self.draft_box: AnnotationBox | None = None
        self.bucket_var = tk.StringVar(value="accepted")
        self.class_var = tk.StringVar(value=_class_option(5))
        self.yolo_autolabel_epochs_var = tk.StringVar(value=str(app.settings.yolo_autolabel_epochs))
        self.yolo_autolabel_conf_var = tk.StringVar(value=str(app.settings.yolo_autolabel_conf))
        self.zoom_var = tk.DoubleVar(value=1.0)
        self._build_widgets()

    def _build_widgets(self) -> None:
        self.columnconfigure(0, weight=0)
        self.columnconfigure(1, weight=1)
        self.rowconfigure(1, weight=1)

        top = ttk.Frame(self)
        top.grid(row=0, column=0, columnspan=2, sticky="ew", padx=8, pady=8)
        ttk.Label(top, text="分桶").pack(side="left")
        ttk.Combobox(top, width=10, textvariable=self.bucket_var, values=("accepted", "review", "fixed"), state="readonly").pack(side="left", padx=4)
        ttk.Button(top, text="加载样本", command=self.load_images).pack(side="left", padx=3)
        ttk.Button(top, text="规则补录并覆盖", command=self.autolabel_current_bucket).pack(side="left", padx=3)
        ttk.Button(top, text="YOLO学习补录", command=self.yolo_autolabel_current_bucket).pack(side="left", padx=3)
        ttk.Label(top, text="学习轮数").pack(side="left", padx=(8, 2))
        ttk.Entry(top, width=5, textvariable=self.yolo_autolabel_epochs_var).pack(side="left", padx=2)
        ttk.Label(top, text="置信度").pack(side="left", padx=(6, 2))
        ttk.Entry(top, width=5, textvariable=self.yolo_autolabel_conf_var).pack(side="left", padx=2)
        ttk.Button(top, text="保存", command=self.save_labels).pack(side="left", padx=3)
        ttk.Button(top, text="移到 fixed", command=self.move_current_to_fixed).pack(side="left", padx=3)
        ttk.Button(top, text="上一张", command=lambda: self.navigate(-1)).pack(side="left", padx=3)
        ttk.Button(top, text="下一张", command=lambda: self.navigate(1)).pack(side="left", padx=3)
        ttk.Button(top, text="移到 rejected", command=self.move_current_to_rejected).pack(side="left", padx=3)
        ttk.Button(top, text="适应窗口", command=self.fit_image).pack(side="left", padx=3)
        ttk.Button(top, text="放大", command=lambda: self.adjust_zoom(1.25)).pack(side="left", padx=3)
        ttk.Button(top, text="缩小", command=lambda: self.adjust_zoom(0.8)).pack(side="left", padx=3)

        side = ttk.Frame(self)
        side.grid(row=1, column=0, sticky="ns", padx=(8, 4), pady=(0, 8))
        side.rowconfigure(5, weight=1)
        ttk.Label(side, text="当前类别").grid(row=0, column=0, sticky="w")
        ttk.Combobox(side, width=34, textvariable=self.class_var, values=[_class_option(i) for i in range(len(LABELS))], state="readonly").grid(row=1, column=0, sticky="ew", pady=(2, 8))
        ttk.Button(side, text="删除选中框", command=self.delete_selected).grid(row=2, column=0, sticky="ew", pady=2)
        ttk.Button(side, text="选中框改为当前类别", command=self.reclass_selected).grid(row=3, column=0, sticky="ew", pady=2)
        self.info_label = ttk.Label(side, text="未加载")
        self.info_label.grid(row=4, column=0, sticky="w", pady=(8, 4))
        self.box_list = tk.Listbox(side, width=38, height=28)
        self.box_list.grid(row=5, column=0, sticky="nsew")
        self.box_list.bind("<<ListboxSelect>>", self._on_list_select)
        ttk.Label(side, text="操作：拖拽空白创建框；拖框内部移动；拖角点缩放；Delete 删除。", wraplength=260).grid(row=6, column=0, sticky="ew", pady=(8, 0))

        canvas_frame = ttk.Frame(self)
        canvas_frame.grid(row=1, column=1, sticky="nsew", padx=(4, 8), pady=(0, 8))
        canvas_frame.columnconfigure(0, weight=1)
        canvas_frame.rowconfigure(0, weight=1)
        self.canvas = tk.Canvas(canvas_frame, background="#2b2b2b", highlightthickness=0)
        self.canvas.grid(row=0, column=0, sticky="nsew")
        xscroll = ttk.Scrollbar(canvas_frame, orient="horizontal", command=self.canvas.xview)
        yscroll = ttk.Scrollbar(canvas_frame, orient="vertical", command=self.canvas.yview)
        xscroll.grid(row=1, column=0, sticky="ew")
        yscroll.grid(row=0, column=1, sticky="ns")
        self.canvas.configure(xscrollcommand=xscroll.set, yscrollcommand=yscroll.set)
        self.canvas.bind("<ButtonPress-1>", self._on_mouse_down)
        self.canvas.bind("<B1-Motion>", self._on_mouse_move)
        self.canvas.bind("<ButtonRelease-1>", self._on_mouse_up)
        self.canvas.bind("<Configure>", lambda _event: self.draw())
        self.bind_all("<Delete>", lambda _event: self.delete_selected())
        self.bind_all("<Control-s>", lambda _event: self.save_labels())

    def load_images(self) -> None:
        if not self._confirm_discard_dirty():
            return
        directory = self._current_directory()
        self.images = image_files(directory)
        self.index = 0 if self.images else -1
        self.dirty = False
        if not self.images:
            self.canvas.delete("all")
            self.info_label.configure(text=f"没有图片：{directory}")
            self.box_list.delete(0, "end")
            return
        self.load_current_image()

    def load_current_image(self) -> None:
        if self.index < 0 or self.index >= len(self.images):
            return
        image_path = self.images[self.index]
        self.image = Image.open(image_path).convert("RGB")
        self.image_size = self.image.size
        self.boxes = _read_annotation_boxes(image_path, self.image_size)
        self.selected_index = None
        self.dirty = False
        self.fit_image()
        self._refresh_box_list()
        self._update_info()

    def navigate(self, delta: int) -> None:
        if not self.images:
            return
        if not self._confirm_discard_dirty():
            return
        self.index = max(0, min(len(self.images) - 1, self.index + delta))
        self.load_current_image()

    def autolabel_current_bucket(self) -> None:
        source = Path(self.app.source_var.get())
        bucket = self.bucket_var.get()

        def action() -> str:
            summary = autolabel_samples(source=source, bucket=bucket, overwrite=True, dry_run=False)
            return (
                "样本自动补录完成："
                f"图片={summary.checked_images}, 更新={summary.updated_images}, 未变={summary.unchanged_images}, "
                f"send_button={summary.added_send_buttons}, customer_message_bubble={summary.added_customer_bubbles}, "
                f"self_message_bubble={summary.added_self_bubbles}"
            )

        self.app.run_background("样本自动补录", action, on_success=self.load_images)

    def yolo_autolabel_current_bucket(self) -> None:
        source = Path(self.app.source_var.get())
        bucket = self.bucket_var.get()
        if not self._confirm_discard_dirty():
            return
        if not messagebox.askyesno("确认 YOLO 学习补录", f"确认使用 accepted 中已标注样本临时训练辅助 YOLO，并补录 {bucket} 分桶？该操作会耗时并写入目标 .txt 标签。"):
            return
        epochs = int(self.yolo_autolabel_epochs_var.get())
        conf = float(self.yolo_autolabel_conf_var.get())

        def action() -> str:
            summary = yolo_autolabel_samples(
                source=source,
                train_bucket="accepted",
                target_bucket=bucket,
                model=self.app.training_tab.model_var.get(),
                epochs=epochs,
                imgsz=int(self.app.training_tab.imgsz_var.get()),
                batch=int(self.app.training_tab.batch_var.get()),
                device=self.app.training_tab.device_var.get(),
                conf=conf,
                artifacts_root=Path(self.app.artifacts_var.get()),
                overwrite_auto=False,
                min_train_samples=5,
            )
            return (
                "YOLO 学习补录完成："
                f"训练样本={summary.train_images}, 目标图片={summary.target_images}, "
                f"预测命中图片={summary.predicted_images}, 更新图片={summary.updated_images}, "
                f"send_button={summary.added_send_buttons}, customer_message_bubble={summary.added_customer_bubbles}, "
                f"self_message_bubble={summary.added_self_bubbles}, 替换旧框={summary.replaced_boxes}"
            )

        self.app.run_background("YOLO 学习补录", action, on_success=self.load_images)

    def save_labels(self) -> None:
        if self.index < 0 or not self.images:
            return
        image_path = self.images[self.index]
        _write_annotation_boxes(image_path, self.boxes, self.image_size)
        self.dirty = False
        self._update_info()
        self.app.append_log(f"已保存标签：{image_path.with_suffix('.txt')}")

    def move_current_to_rejected(self) -> None:
        if self.index < 0 or not self.images:
            return
        if not messagebox.askyesno("确认隔离", "确认把当前样本三件套移到 rejected/gui-rejected？"):
            return
        if self.dirty and not self._confirm_discard_dirty():
            return
        image_path = self.images[self.index]
        source_root = Path(self.app.source_var.get()).resolve()
        rejected_dir = _rejected_dir_for(image_path, source_root)
        rejected_dir.mkdir(parents=True, exist_ok=True)
        for suffix in (image_path.suffix, ".txt", ".json"):
            path = image_path.with_suffix(suffix)
            if path.exists():
                target = _unique_path(rejected_dir / path.name)
                shutil.move(str(path), str(target))
        self.app.append_log(f"已隔离样本：{image_path.name}")
        self.images.pop(self.index)
        if self.index >= len(self.images):
            self.index = len(self.images) - 1
        if self.images:
            self.load_current_image()
        else:
            self.canvas.delete("all")
            self.box_list.delete(0, "end")
            self.info_label.configure(text="当前分桶已无图片")

    def move_current_to_fixed(self) -> None:
        if self.index < 0 or not self.images:
            return
        if not messagebox.askyesno("确认移到 fixed", "确认把当前样本三件套移到 fixed？"):
            return
        if self.dirty and not self._confirm_discard_dirty():
            return
        self._move_current_sample_to_bucket("fixed")

    def _move_current_sample_to_bucket(self, bucket: str) -> None:
        image_path = self.images[self.index]
        source_root = Path(self.app.source_var.get()).resolve()
        target_dir = _source_bucket_path(source_root, bucket)
        target_dir.mkdir(parents=True, exist_ok=True)
        for suffix in (image_path.suffix, ".txt", ".json"):
            path = image_path.with_suffix(suffix)
            if path.exists():
                target = _unique_path(target_dir / path.name)
                shutil.move(str(path), str(target))
        self.app.append_log(f"已移动样本到 {bucket}：{image_path.name}")
        self.images.pop(self.index)
        if self.index >= len(self.images):
            self.index = len(self.images) - 1
        if self.images:
            self.load_current_image()
        else:
            self.canvas.delete("all")
            self.box_list.delete(0, "end")
            self.info_label.configure(text="当前分桶已无图片")

    def fit_image(self) -> None:
        if self.image is None:
            return
        canvas_width = max(200, self.canvas.winfo_width())
        canvas_height = max(200, self.canvas.winfo_height())
        image_width, image_height = self.image_size
        self.scale = min(canvas_width / image_width, canvas_height / image_height)
        self.scale = max(0.05, min(5.0, self.scale))
        self.offset_x = 20
        self.offset_y = 20
        self.draw()

    def adjust_zoom(self, factor: float) -> None:
        if self.image is None:
            return
        self.scale = max(0.05, min(8.0, self.scale * factor))
        self.draw()

    def delete_selected(self) -> None:
        if self.selected_index is None:
            return
        del self.boxes[self.selected_index]
        self.selected_index = None
        self.dirty = True
        self._refresh_box_list()
        self.draw()
        self._update_info()

    def reclass_selected(self) -> None:
        if self.selected_index is None:
            return
        class_id = _parse_class_option(self.class_var.get())
        box = self.boxes[self.selected_index]
        self.boxes[self.selected_index] = AnnotationBox(class_id, box.left, box.top, box.right, box.bottom)
        self.dirty = True
        self._refresh_box_list()
        self.draw()

    def draw(self) -> None:
        self.canvas.delete("all")
        if self.image is None:
            return
        image_width, image_height = self.image_size
        display_size = (max(1, round(image_width * self.scale)), max(1, round(image_height * self.scale)))
        display_image = self.image.resize(display_size, _resampling_lanczos())
        self.photo = ImageTk.PhotoImage(display_image)
        self.canvas.create_image(self.offset_x, self.offset_y, anchor="nw", image=self.photo)
        for index, box in enumerate(self.boxes):
            self._draw_box(index, box)
        if self.draft_box is not None:
            self._draw_box(None, self.draft_box, dashed=True)
        self.canvas.configure(scrollregion=(0, 0, self.offset_x + display_size[0] + 40, self.offset_y + display_size[1] + 40))

    def _draw_box(self, index: int | None, box: AnnotationBox, *, dashed: bool = False) -> None:
        color = BOX_COLORS[box.class_id % len(BOX_COLORS)]
        width = 3 if index == self.selected_index else 2
        x1, y1 = self._to_canvas(box.left, box.top)
        x2, y2 = self._to_canvas(box.right, box.bottom)
        dash = (6, 3) if dashed else None
        self.canvas.create_rectangle(x1, y1, x2, y2, outline=color, width=width, dash=dash)
        label = LABELS[box.class_id]
        text_id = self.canvas.create_text(x1 + 4, y1 + 4, text=label, fill="white", anchor="nw")
        bbox = self.canvas.bbox(text_id)
        if bbox:
            self.canvas.create_rectangle(bbox, fill=color, outline=color)
            self.canvas.tag_raise(text_id)
        if index == self.selected_index:
            for hx, hy in ((x1, y1), (x2, y1), (x1, y2), (x2, y2)):
                self.canvas.create_rectangle(hx - 4, hy - 4, hx + 4, hy + 4, fill=color, outline="white")

    def _on_mouse_down(self, event: tk.Event) -> None:
        if self.image is None:
            return
        point = self._event_to_image(event)
        if point is None:
            return
        self.drag_start = point
        hit = self._hit_test(point)
        if hit is not None:
            self.selected_index, mode = hit
            self.drag_mode = mode
            self.drag_box = self.boxes[self.selected_index]
        else:
            self.selected_index = None
            self.drag_mode = "create"
            class_id = _parse_class_option(self.class_var.get())
            self.draft_box = AnnotationBox(class_id, point[0], point[1], point[0], point[1])
        self._refresh_box_list()
        self.draw()

    def _on_mouse_move(self, event: tk.Event) -> None:
        if self.image is None or self.drag_mode is None or self.drag_start is None:
            return
        point = self._event_to_image(event)
        if point is None:
            return
        image_width, image_height = self.image_size
        if self.drag_mode == "create" and self.draft_box is not None:
            self.draft_box = AnnotationBox(
                self.draft_box.class_id,
                _clamp(min(self.drag_start[0], point[0]), 0, image_width),
                _clamp(min(self.drag_start[1], point[1]), 0, image_height),
                _clamp(max(self.drag_start[0], point[0]), 0, image_width),
                _clamp(max(self.drag_start[1], point[1]), 0, image_height),
            )
        elif self.selected_index is not None and self.drag_box is not None:
            dx = point[0] - self.drag_start[0]
            dy = point[1] - self.drag_start[1]
            box = self.drag_box
            if self.drag_mode == "move":
                self.boxes[self.selected_index] = _move_box(box, dx, dy, image_width, image_height)
            else:
                self.boxes[self.selected_index] = _resize_box(box, self.drag_mode, dx, dy, image_width, image_height)
            self.dirty = True
        self.draw()

    def _on_mouse_up(self, _event: tk.Event) -> None:
        if self.drag_mode == "create" and self.draft_box is not None:
            if self.draft_box.width >= 4 and self.draft_box.height >= 4:
                self.boxes.append(self.draft_box)
                self.selected_index = len(self.boxes) - 1
                self.dirty = True
        self.drag_mode = None
        self.drag_start = None
        self.drag_box = None
        self.draft_box = None
        self._refresh_box_list()
        self.draw()
        self._update_info()

    def _hit_test(self, point: tuple[float, float]) -> tuple[int, str] | None:
        radius = max(8 / self.scale, 3)
        for index in range(len(self.boxes) - 1, -1, -1):
            box = self.boxes[index]
            corners = {
                "nw": (box.left, box.top),
                "ne": (box.right, box.top),
                "sw": (box.left, box.bottom),
                "se": (box.right, box.bottom),
            }
            for mode, corner in corners.items():
                if abs(point[0] - corner[0]) <= radius and abs(point[1] - corner[1]) <= radius:
                    return index, mode
            if box.left <= point[0] <= box.right and box.top <= point[1] <= box.bottom:
                return index, "move"
        return None

    def _event_to_image(self, event: tk.Event) -> tuple[float, float] | None:
        x = self.canvas.canvasx(event.x)
        y = self.canvas.canvasy(event.y)
        image_x = (x - self.offset_x) / self.scale
        image_y = (y - self.offset_y) / self.scale
        image_width, image_height = self.image_size
        if image_x < 0 or image_y < 0 or image_x > image_width or image_y > image_height:
            return None
        return image_x, image_y

    def _to_canvas(self, x: float, y: float) -> tuple[float, float]:
        return self.offset_x + x * self.scale, self.offset_y + y * self.scale

    def _on_list_select(self, _event: tk.Event) -> None:
        selection = self.box_list.curselection()
        if not selection:
            return
        self.selected_index = int(selection[0])
        self.class_var.set(_class_option(self.boxes[self.selected_index].class_id))
        self.draw()

    def _refresh_box_list(self) -> None:
        self.box_list.delete(0, "end")
        for index, box in enumerate(self.boxes):
            self.box_list.insert("end", f"{index + 1:02d} {LABELS[box.class_id]}  {box.width:.0f}x{box.height:.0f}")
        if self.selected_index is not None and self.selected_index < self.box_list.size():
            self.box_list.selection_set(self.selected_index)
            self.box_list.see(self.selected_index)

    def _update_info(self) -> None:
        if self.index < 0 or not self.images:
            self.info_label.configure(text="未加载")
            return
        dirty_mark = "*" if self.dirty else ""
        self.info_label.configure(text=f"{self.index + 1}/{len(self.images)} {self.images[self.index].name}{dirty_mark}\n框数：{len(self.boxes)}")

    def _current_directory(self) -> Path:
        source = Path(self.app.source_var.get())
        bucket = self.bucket_var.get()
        if bucket == "source":
            return source
        if source.name.lower() == bucket:
            return source
        return source / bucket

    def _confirm_discard_dirty(self) -> bool:
        if not self.dirty:
            return True
        answer = messagebox.askyesnocancel("标签未保存", "当前标签有修改，是否先保存？")
        if answer is None:
            return False
        if answer:
            self.save_labels()
        return True


class TrainingTab(ttk.Frame):
    def __init__(self, parent: ttk.Notebook, app: VisionTrainerApp) -> None:
        super().__init__(parent)
        self.app = app
        self.bucket_var = tk.StringVar(value=app.settings.bucket)
        self.min_samples_var = tk.StringVar(value=str(app.settings.min_samples))
        self.review_count_var = tk.StringVar(value=str(app.settings.review_count))
        self.model_var = tk.StringVar(value=app.settings.model)
        self.epochs_var = tk.StringVar(value=str(app.settings.epochs))
        self.imgsz_var = tk.StringVar(value=str(app.settings.imgsz))
        self.batch_var = tk.StringVar(value=str(app.settings.batch))
        self.device_var = tk.StringVar(value=app.settings.device)
        self.version_var = tk.StringVar(value="")
        self.predict_conf_var = tk.StringVar(value=str(app.settings.predict_conf))
        self.autolabel_overwrite_var = tk.BooleanVar(value=app.settings.autolabel_overwrite)
        self.no_autolabel_var = tk.BooleanVar(value=False)
        self.no_install_var = tk.BooleanVar(value=False)
        self._build_widgets()

    def _build_widgets(self) -> None:
        self.columnconfigure(1, weight=1)
        row = 0
        for label, variable, browse_kind in (
            ("学习样本目录", self.app.source_var, "dir"),
            ("数据集目录", self.app.dataset_var, "dir"),
            ("产物根目录", self.app.artifacts_var, "dir"),
            ("候选模型根目录", self.app.candidate_var, "dir"),
        ):
            ttk.Label(self, text=label).grid(row=row, column=0, sticky="w", padx=8, pady=4)
            ttk.Entry(self, textvariable=variable).grid(row=row, column=1, sticky="ew", padx=4, pady=4)
            ttk.Button(self, text="浏览", command=lambda v=variable, k=browse_kind: browse_path(v, k)).grid(row=row, column=2, padx=8, pady=4)
            row += 1

        fields = ttk.LabelFrame(self, text="训练参数")
        fields.grid(row=row, column=0, columnspan=3, sticky="ew", padx=8, pady=8)
        for col in range(6):
            fields.columnconfigure(col, weight=1)
        pairs = [
            ("分桶", self.bucket_var, ("accepted", "fixed", "accepted-fixed", "review", "all")),
            ("最小样本", self.min_samples_var, None),
            ("复核数量", self.review_count_var, None),
            ("基础模型", self.model_var, None),
            ("epochs", self.epochs_var, None),
            ("imgsz", self.imgsz_var, None),
            ("batch", self.batch_var, None),
            ("device", self.device_var, None),
            ("version", self.version_var, None),
            ("predict_conf", self.predict_conf_var, None),
        ]
        for idx, (label, variable, values) in enumerate(pairs):
            r = idx // 2
            c = (idx % 2) * 3
            ttk.Label(fields, text=label).grid(row=r, column=c, sticky="w", padx=6, pady=4)
            if values:
                ttk.Combobox(fields, textvariable=variable, values=values, state="readonly").grid(row=r, column=c + 1, sticky="ew", padx=6, pady=4)
            else:
                ttk.Entry(fields, textvariable=variable).grid(row=r, column=c + 1, sticky="ew", padx=6, pady=4)

        checks = ttk.Frame(fields)
        checks.grid(row=5, column=0, columnspan=6, sticky="w", padx=6, pady=4)
        ttk.Checkbutton(checks, text="训练前覆盖重算自动标签", variable=self.autolabel_overwrite_var).pack(side="left", padx=6)
        ttk.Checkbutton(checks, text="跳过自动补录", variable=self.no_autolabel_var).pack(side="left", padx=6)
        ttk.Checkbutton(checks, text="不安装候选", variable=self.no_install_var).pack(side="left", padx=6)

        row += 1
        actions = ttk.Frame(self)
        actions.grid(row=row, column=0, columnspan=3, sticky="ew", padx=8, pady=8)
        ttk.Button(actions, text="只检查样本数量", command=self.run_dry).pack(side="left", padx=4)
        ttk.Button(actions, text="安全自测", command=self.run_skip_train).pack(side="left", padx=4)
        ttk.Button(actions, text="开始训练候选模型", command=self.run_train).pack(side="left", padx=4)
        ttk.Button(actions, text="打开复核包", command=lambda: self.open_artifact("wechat-layout-review")).pack(side="left", padx=4)
        ttk.Button(actions, text="打开预测图", command=lambda: self.open_artifact("wechat-layout-predict")).pack(side="left", padx=4)
        ttk.Button(actions, text="打开候选产物", command=lambda: self.open_artifact("wechat-layout-candidates")).pack(side="left", padx=4)

        row += 1
        note = ttk.Label(
            self,
            text="说明：开始训练和候选转正都会二次确认。active-learn 默认只使用 accepted；review 只建议清洗后显式选择。",
            wraplength=900,
        )
        note.grid(row=row, column=0, columnspan=3, sticky="w", padx=8, pady=8)

    def run_dry(self) -> None:
        self.app.run_command("主动学习样本检查", self._args(dry_run=True))

    def run_skip_train(self) -> None:
        self.app.run_command("主动学习安全自测", self._args(skip_train=True))

    def run_train(self) -> None:
        self.app.run_command("主动学习正式训练", self._args(), confirm="确认开始训练候选模型？训练会占用 GPU/CPU，耗时较长。")

    def open_artifact(self, kind: str) -> None:
        version = self.version_var.get().strip()
        root = Path(self.app.artifacts_var.get()) / kind
        if version:
            open_path(root / version)
        else:
            open_path(root)

    def _args(self, *, dry_run: bool = False, skip_train: bool = False) -> list[str]:
        args = [
            "active-learn",
            "--source", self.app.source_var.get(),
            "--dataset", self.app.dataset_var.get(),
            "--bucket", self.bucket_var.get(),
            "--min-samples", self.min_samples_var.get(),
            "--review-count", self.review_count_var.get(),
            "--model", self.model_var.get(),
            "--epochs", self.epochs_var.get(),
            "--imgsz", self.imgsz_var.get(),
            "--batch", self.batch_var.get(),
            "--device", self.device_var.get(),
            "--predict-conf", self.predict_conf_var.get(),
            "--artifacts-root", self.app.artifacts_var.get(),
            "--candidate-root", self.app.candidate_var.get(),
        ]
        version = self.version_var.get().strip()
        if version:
            args.extend(["--version", version])
        if dry_run:
            args.append("--dry-run")
        if skip_train:
            args.append("--skip-train")
        if self.no_install_var.get():
            args.append("--no-install-candidate")
        if self.no_autolabel_var.get():
            args.append("--no-autolabel-samples")
        if self.autolabel_overwrite_var.get():
            args.append("--autolabel-overwrite")
        return args


class ModelTab(ttk.Frame):
    def __init__(self, parent: ttk.Notebook, app: VisionTrainerApp) -> None:
        super().__init__(parent)
        self.app = app
        self.columnconfigure(0, weight=1)
        self.rowconfigure(1, weight=1)
        actions = ttk.Frame(self)
        actions.grid(row=0, column=0, sticky="ew", padx=8, pady=8)
        ttk.Button(actions, text="刷新候选", command=self.refresh).pack(side="left", padx=4)
        ttk.Button(actions, text="打开候选目录", command=self.open_selected).pack(side="left", padx=4)
        ttk.Button(actions, text="转正选中候选", command=self.promote_selected).pack(side="left", padx=4)
        ttk.Button(actions, text="打开正式模型目录", command=lambda: open_path(default_local_install_dir())).pack(side="left", padx=4)
        self.listbox = tk.Listbox(self)
        self.listbox.grid(row=1, column=0, sticky="nsew", padx=8, pady=(0, 8))
        self.info = tk.Text(self, height=10, wrap="word")
        self.info.grid(row=2, column=0, sticky="ew", padx=8, pady=(0, 8))
        self.listbox.bind("<<ListboxSelect>>", lambda _event: self.show_selected())
        self.candidates: list[Path] = []
        self.refresh()

    def refresh(self) -> None:
        self.candidates = _candidate_dirs(Path(self.app.candidate_var.get()))
        self.listbox.delete(0, "end")
        for path in self.candidates:
            self.listbox.insert("end", path.name)
        self.show_selected()

    def selected_path(self) -> Path | None:
        selection = self.listbox.curselection()
        if not selection:
            return None
        return self.candidates[int(selection[0])]

    def show_selected(self) -> None:
        path = self.selected_path()
        lines: list[str]
        if path is None:
            lines = [f"正式模型目录：{default_local_install_dir()}"]
        else:
            lines = [f"候选目录：{path}"]
            for name in PACKAGE_FILES:
                lines.append(f"  {name}: {'存在' if (path / name).exists() else '缺失'}")
            report = path / "active-learn-report.json"
            if report.exists():
                lines.append(f"  报告：{report}")
        self.info.delete("1.0", "end")
        self.info.insert("1.0", "\n".join(lines))

    def open_selected(self) -> None:
        path = self.selected_path()
        if path:
            open_path(path)
        else:
            open_path(Path(self.app.candidate_var.get()))

    def promote_selected(self) -> None:
        path = self.selected_path()
        if path is None:
            messagebox.showwarning("未选择候选", "请先选择一个候选模型。")
            return
        if not messagebox.askyesno("确认转正", f"确认把候选模型 {path.name} 转正到正式模型目录？"):
            return

        def action() -> str:
            summary = promote_candidate(str(path), install_local=True, candidate_root=Path(self.app.candidate_var.get()))
            return f"候选模型已转正：{summary.candidate_dir} -> {summary.installed_dir}，文件={', '.join(summary.copied_files)}"

        self.app.run_background("候选模型转正", action, on_success=self.refresh)


class SettingsTab(ttk.Frame):
    def __init__(self, parent: ttk.Notebook, app: VisionTrainerApp) -> None:
        super().__init__(parent)
        self.app = app
        self.columnconfigure(1, weight=1)
        row = 0
        for label, variable in (
            ("学习样本目录", app.source_var),
            ("数据集目录", app.dataset_var),
            ("产物根目录", app.artifacts_var),
            ("候选模型根目录", app.candidate_var),
        ):
            ttk.Label(self, text=label).grid(row=row, column=0, sticky="w", padx=8, pady=6)
            ttk.Entry(self, textvariable=variable).grid(row=row, column=1, sticky="ew", padx=4, pady=6)
            ttk.Button(self, text="浏览", command=lambda v=variable: browse_path(v, "dir")).grid(row=row, column=2, padx=8, pady=6)
            row += 1
        ttk.Button(self, text="保存设置", command=app.save_settings).grid(row=row, column=0, sticky="w", padx=8, pady=12)
        ttk.Label(self, text=f"配置文件：{CONFIG_PATH}").grid(row=row + 1, column=0, columnspan=3, sticky="w", padx=8, pady=6)


def load_settings() -> GuiSettings:
    defaults = default_settings()
    if not CONFIG_PATH.exists():
        return defaults
    try:
        data = json.loads(CONFIG_PATH.read_text(encoding="utf-8-sig"))
    except Exception:
        return defaults
    merged = asdict(defaults)
    merged.update({key: value for key, value in data.items() if key in merged})
    return GuiSettings(**merged)


def save_settings(settings: GuiSettings) -> None:
    CONFIG_PATH.parent.mkdir(parents=True, exist_ok=True)
    CONFIG_PATH.write_text(json.dumps(asdict(settings), ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


def default_settings() -> GuiSettings:
    local_app_data = os.environ.get("LOCALAPPDATA")
    if local_app_data:
        source = Path(local_app_data) / "AIChat" / "RpaClient" / "learning-samples"
    else:
        source = Path.home() / "AppData" / "Local" / "AIChat" / "RpaClient" / "learning-samples"
    repo_root = tool_root().parents[1]
    return GuiSettings(
        source=str(source),
        dataset=str(repo_root / "datasets" / "wechat-layout"),
        artifacts_root=str(default_artifacts_root()),
        candidate_root=str(default_candidate_root()),
    )


def browse_path(variable: tk.StringVar, kind: str) -> None:
    if kind == "dir":
        selected = filedialog.askdirectory(initialdir=variable.get() or str(tool_root()))
    else:
        selected = filedialog.askopenfilename(initialdir=variable.get() or str(tool_root()))
    if selected:
        variable.set(selected)


def open_path(path: str | Path) -> None:
    target = Path(path)
    target.mkdir(parents=True, exist_ok=True) if not target.suffix else None
    if os.name == "nt":
        os.startfile(str(target))  # type: ignore[attr-defined]
    else:
        subprocess.Popen(["xdg-open", str(target)])


def _read_annotation_boxes(image_path: Path, image_size: tuple[int, int]) -> list[AnnotationBox]:
    label_path = image_path.with_suffix(".txt")
    if not label_path.exists():
        return []
    image_width, image_height = image_size
    boxes: list[AnnotationBox] = []
    for line in label_path.read_text(encoding="utf-8-sig").splitlines():
        stripped = line.strip()
        if not stripped:
            continue
        box = AnnotationBox.from_yolo_line(stripped, image_width, image_height)
        if box is not None:
            boxes.append(box)
    return boxes


def _write_annotation_boxes(image_path: Path, boxes: list[AnnotationBox], image_size: tuple[int, int]) -> None:
    image_width, image_height = image_size
    normalized = [box for box in boxes if box.width > 0 and box.height > 0]
    normalized.sort(key=lambda item: (item.class_id, item.top, item.left))
    text = "\n".join(box.normalized_line(image_width, image_height) for box in normalized) + "\n"
    image_path.with_suffix(".txt").write_text(text, encoding="utf-8")
    write_labels_file(image_path.parent / "classes.txt")


def _label_stats(directory: Path) -> dict[str, object]:
    images = image_files(directory)
    class_counts = {label: 0 for label in LABELS}
    coverage = {label: 0 for label in LABELS}
    missing = 0
    empty = 0
    for image_path in images:
        label_path = image_path.with_suffix(".txt")
        if not label_path.exists():
            missing += 1
            continue
        lines = [line.strip() for line in label_path.read_text(encoding="utf-8-sig").splitlines() if line.strip()]
        if not lines:
            empty += 1
            continue
        present: set[int] = set()
        for line in lines:
            parts = line.split()
            if len(parts) == 5 and parts[0].isdigit():
                class_id = int(parts[0])
                if 0 <= class_id < len(LABELS):
                    class_counts[LABELS[class_id]] += 1
                    present.add(class_id)
        for class_id in present:
            coverage[LABELS[class_id]] += 1
    return {"images": len(images), "missing": missing, "empty": empty, "class_counts": class_counts, "coverage": coverage}


def _format_sample_stats(name: str, directory: Path, stats: dict[str, object]) -> list[str]:
    lines = [f"  {name}: {directory}", f"    图片={stats['images']}，缺标签={stats['missing']}，空标签={stats['empty']}"]
    class_counts: dict[str, int] = stats["class_counts"]  # type: ignore[assignment]
    coverage: dict[str, int] = stats["coverage"]  # type: ignore[assignment]
    for label in LABELS:
        lines.append(f"    {label}: 标签={class_counts[label]}，覆盖图片={coverage[label]}")
    return lines


def _source_bucket_path(source: str | Path, bucket: str) -> Path:
    source_path = Path(source)
    if source_path.name.lower() == bucket:
        return source_path
    return source_path / bucket


def _candidate_dirs(candidate_root: Path) -> list[Path]:
    if not candidate_root.exists():
        return []
    return sorted((path for path in candidate_root.iterdir() if path.is_dir()), key=lambda item: item.stat().st_mtime, reverse=True)


def _class_option(index: int) -> str:
    return f"{index} {LABELS[index]}"


def _parse_class_option(value: str) -> int:
    try:
        class_id = int(value.split()[0])
    except Exception:
        return 0
    return max(0, min(len(LABELS) - 1, class_id))


def _move_box(box: AnnotationBox, dx: float, dy: float, image_width: int, image_height: int) -> AnnotationBox:
    width = box.width
    height = box.height
    left = _clamp(box.left + dx, 0, image_width - width)
    top = _clamp(box.top + dy, 0, image_height - height)
    return AnnotationBox(box.class_id, left, top, left + width, top + height)


def _resize_box(box: AnnotationBox, mode: str, dx: float, dy: float, image_width: int, image_height: int) -> AnnotationBox:
    left, top, right, bottom = box.left, box.top, box.right, box.bottom
    if "n" in mode:
        top += dy
    if "s" in mode:
        bottom += dy
    if "w" in mode:
        left += dx
    if "e" in mode:
        right += dx
    left = _clamp(left, 0, image_width)
    right = _clamp(right, 0, image_width)
    top = _clamp(top, 0, image_height)
    bottom = _clamp(bottom, 0, image_height)
    if right < left:
        left, right = right, left
    if bottom < top:
        top, bottom = bottom, top
    return AnnotationBox(box.class_id, left, top, right, bottom)


def _rejected_dir_for(image_path: Path, source_root: Path) -> Path:
    try:
        image_path.relative_to(source_root)
        return source_root / "rejected" / "gui-rejected"
    except ValueError:
        parent = image_path.parent
        if parent.name in {"accepted", "review", "fixed"}:
            return parent.parent / "rejected" / "gui-rejected"
        return parent / "rejected" / "gui-rejected"


def _unique_path(path: Path) -> Path:
    if not path.exists():
        return path
    for index in range(1, 10000):
        candidate = path.with_name(f"{path.stem}-{index}{path.suffix}")
        if not candidate.exists():
            return candidate
    raise VisionTrainerError(f"无法生成唯一文件名：{path}")


def _command_text(command: list[str]) -> str:
    return " ".join(f'"{part}"' if " " in part else part for part in command)


def _now_text() -> str:
    return datetime.now().strftime("%H:%M:%S")


def _clamp(value: float, minimum: float, maximum: float) -> float:
    return max(minimum, min(maximum, value))


def _resampling_lanczos():
    try:
        return Image.Resampling.LANCZOS
    except AttributeError:
        return Image.LANCZOS


if __name__ == "__main__":
    raise SystemExit(main())
