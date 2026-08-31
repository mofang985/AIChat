@echo off
setlocal
cd /d "%~dp0"

if exist ".venv\Scripts\python.exe" (
  ".venv\Scripts\python.exe" -m aichat_vision gui
) else (
  python -m aichat_vision gui
)

if errorlevel 1 (
  echo.
  echo AIChat Vision Trainer 启动失败，请检查 Python 虚拟环境和依赖安装。
  pause
)
