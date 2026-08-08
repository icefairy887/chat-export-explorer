# UltraChat Demo Data

The repository includes `import_ultrachat.py` so the project can be demonstrated with public conversation data rather than private ChatGPT exports.

## Build a demo database

```powershell
python import_ultrachat.py --limit 1000 --database demo_chat_history.db
```

The importer converts UltraChat conversations into the project's existing `conversations` and `messages` schema while preserving conversation boundaries, message ordering, user/assistant roles, and source text.

## Deterministic import

Conversation and message identifiers are generated deterministically so rerunning the importer does not duplicate rows.

Synthetic timestamps are generated when source timestamps are unavailable so chronological analysis remains possible.

## Run the app against demo data

```powershell
$env:CHAT_DB="demo_chat_history.db"
python app.py
```

Then open:

```text
http://127.0.0.1:5000
```

## Why the database is not committed

`demo_chat_history.db` is generated data. Keeping it out of Git makes the repository smaller and keeps the demo reproducible from the importer.

## Suggested demo sizes

- 100 conversations: fast smoke test
- 1,000 conversations: useful local demo
- larger samples: stress testing for embeddings, exchange construction, and future clustering
