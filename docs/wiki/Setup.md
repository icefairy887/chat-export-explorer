# Setup

## Requirements

- Python 3.12+ recommended
- Git
- Optional Node.js tooling
- Internet access only when downloading public datasets or the local embedding model for the first time

## Install

```powershell
python -m pip install -r requirements.txt
```

## Private archive workflow

Import a ChatGPT export into a local SQLite database:

```powershell
python import_export.py conversations.json --database chat_history.db
```

Build daily trajectory states:

```powershell
python build_trajectories.py
```

Run the Flask app:

```powershell
python app.py
```

Then open:

```text
http://127.0.0.1:5000
```

## Public demo workflow

Build a separate demo database from UltraChat:

```powershell
python import_ultrachat.py --limit 1000 --database demo_chat_history.db
```

Run the app against it:

```powershell
$env:CHAT_DB="demo_chat_history.db"
python app.py
```

Clear the override later:

```powershell
Remove-Item Env:CHAT_DB
```

## Semantic search

Build message embeddings and run a search:

```powershell
python semantic_search.py --rebuild "A company wants to speak with me about joining their team" --limit 10
```

## Exchange construction

After message embeddings exist:

```powershell
python build_exchanges.py
```

The exchange layer groups contiguous user input with the assistant reply-run that follows it while preserving role provenance.
