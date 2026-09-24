@echo off
:: Shortcut kept for double-clicking from the repo root.
:: The real build/deploy logic lives in scripts\build_mod.bat (single source of truth).
call "%~dp0scripts\build_mod.bat" %*
