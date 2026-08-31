from __future__ import annotations

from pathlib import Path


def tool_root() -> Path:
    return Path(__file__).resolve().parents[1]


def resolve_path(value: str | Path) -> Path:
    return Path(value).expanduser().resolve()
