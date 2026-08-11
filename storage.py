from __future__ import annotations

import os
import shutil
import sys
from pathlib import Path


def get_project_dir() -> Path:
    if getattr(sys, "frozen", False):
        return Path(sys.executable).resolve().parent
    return Path(__file__).resolve().parent


def get_app_data_dir() -> Path:
    local_app_data = os.environ.get("LOCALAPPDATA")

    if local_app_data:
        app_dir = Path(local_app_data) / "ChatExportExplorer"
    else:
        app_dir = Path.home() / ".chat_export_explorer"

    app_dir.mkdir(parents=True, exist_ok=True)

    return app_dir


def get_database_path() -> Path:
    # Explicit override still wins if somebody sets CHAT_DB.
    override = os.environ.get("CHAT_DB")

    if override:
        return Path(override)

    app_dir = get_app_data_dir()
    database = app_dir / "chat_history.db"

    # First desktop launch:
    # copy the existing V4 database into the user's app-data folder.
    old_database = get_project_dir() / "chat_history.db"

    if not database.exists() and old_database.exists():
        shutil.copy2(old_database, database)

    return database
