@echo off
setlocal
cd /d "%~dp0"
python -m pip install -r requirements.txt
if not exist chat_history.db (
  if not exist conversations.json (
    echo conversations.json was not found in this folder.
    pause
    exit /b 1
  )
  python import_export.py conversations.json
)
python build_trajectories.py
python app.py
pause
