from pathlib import Path
import sqlite3
from trajectory import build_states

DB = Path(__file__).resolve().parent / "chat_history.db"
if not DB.exists():
    raise SystemExit("chat_history.db not found. Import conversations.json first.")
conn = sqlite3.connect(DB)
conn.row_factory = sqlite3.Row
try:
    count = build_states(conn)
finally:
    conn.close()
print(f"Trajectory states built: {count:,} days")
