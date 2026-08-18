# Chat Export Explorer

Local-first semantic analysis for conversation archives.

## Lumina desktop analyzer preview

The repository now also contains the source for **Lumina**, the .NET Windows
longitudinal-analysis interface, under [`lumina-dotnet`](lumina-dotnet/README.md).
Chat Export Explorer remains the archive, browse, search, and source-inspection
layer. Lumina adds timeline-event persistence, local semantic change detection,
evidence-linked findings, and an optional cloud reasoning boundary.

The Windows Lumina Suite package bundles both interfaces:

```text
Lumina.exe
Models/
Archive Explorer/Chat Export Explorer.exe
```

Private exports and generated databases remain local and are not part of the
repository or release source.

Chat Export Explorer turns exported chat history into a searchable, inspectable analysis workspace using Flask, SQLite, local sentence embeddings, trajectory comparison, exchange-level structure, and evidence-linked semantic retrieval.

The project is designed around one rule: **analysis should stay traceable to the source messages that produced it.**

## Documentation

Start with the [project wiki](docs/wiki/Home.md), then jump into [Setup](docs/wiki/Setup.md), [Architecture](docs/wiki/Architecture.md), [Data and Privacy](docs/wiki/Data-and-Privacy.md), [Semantic Pipeline](docs/wiki/Semantic-Pipeline.md), [UltraChat Demo Data](docs/wiki/UltraChat-Demo-Data.md), or the [Roadmap](docs/wiki/Roadmap.md).

## What it does

- Full-text search across conversations with SQLite FTS5
- Browse complete conversation threads
- Track exact phrases over time with the Pattern Explorer
- Build daily trajectory states and compare historically similar periods
- Compare decision branches against historical outcomes
- Generate local semantic embeddings with `all-MiniLM-L6-v2`
- Search individual messages by semantic meaning instead of exact keywords
- Group user input and assistant replies into exchange-level units while preserving role provenance
- Import public UltraChat conversations into a separate demo database for safe testing

## Architecture

```text
Conversation archive
        |
        v
SQLite conversations + messages
        |
        +--> FTS5 search / browse / phrase patterns
        |
        +--> local message embeddings
        |        |
        |        +--> semantic message search
        |        +--> exchange centroids
        |
        +--> daily trajectory states
                 |
                 +--> historical similarity
                 +--> decision simulator
```

Two representations are intentionally preserved:

- **User-authored evidence** is kept separate for observation-level analysis.
- **Assistant-authored text** may provide conversational context, but should not be treated as independent evidence that a user pattern exists.

## Privacy model

Real conversation databases are local-only and ignored by Git.

Do **not** commit:

```text
chat_history.db
demo_chat_history.db
conversations.json
*.db
```

For public demonstrations, use the included UltraChat importer to build a separate synthetic/public database.

## Quick start

Current desktop release: **1.1.0**

The Windows desktop build stores the active database in
`%LOCALAPPDATA%\ChatExportExplorer\chat_history.db`. On first launch it migrates
an existing `chat_history.db` placed beside the executable, leaving the source
file untouched.

### 1. Install dependencies

```powershell
python -m pip install -r requirements.txt
```

### 2. Use your own ChatGPT export

Import `conversations.json`:

```powershell
python import_export.py conversations.json --database chat_history.db
```

Build trajectory states:

```powershell
python build_trajectories.py
```

Run the app:

```powershell
python app.py
```

Open:

```text
http://127.0.0.1:5000
```

### Build the Windows desktop app

Install PyInstaller, then build from the repository root:

```powershell
python -m pip install pyinstaller
python -m PyInstaller --clean "Chat Export Explorer.spec"
```

The distributable folder is written to `dist\Chat Export Explorer`.

## Public demo data with UltraChat

Create a separate demo database without exposing private conversation history:

```powershell
python import_ultrachat.py --limit 1000 --database demo_chat_history.db
```

Run the app against the demo DB:

```powershell
$env:CHAT_DB="demo_chat_history.db"
python app.py
```

Return to the default database later with:

```powershell
Remove-Item Env:CHAT_DB
```

## Semantic message search

Build local message embeddings and run a query:

```powershell
python semantic_search.py --rebuild "A company wants to speak with me about joining their team" --limit 10
```

Embeddings are computed locally with Sentence Transformers and stored as float32 BLOBs in SQLite.

The semantic layer is useful for queries whose meaning is present in the archive even when the exact words are not.

## Exchange layer

`build_exchanges.py` groups contiguous user input with the assistant reply or reply-run that follows it.

Each exchange preserves:

- conversation ID
- timestamps
- original message IDs
- message roles
- user-only centroid
- full-context centroid
- representative user message

This layer exists because a single message is often too small to represent a meaningful conversational event.

## Web features

| Route | Purpose |
|---|---|
| `/` | Dashboard and archive statistics |
| `/search` | Full-text semantic-independent archive search |
| `/browse` | Browse conversations |
| `/patterns` | Track exact phrase frequency and locations |
| `/trajectory` | Compare a current state with historically similar daily states |
| `/simulate` | Compare historical outcomes for different action branches |
| `/health` | Database/server health check |

## Analysis philosophy

The project separates **retrieval** from **interpretation**.

Semantic embeddings answer:

> Which historical messages or states are actually similar in meaning?

Trajectory features and downstream metrics answer:

> What changed afterward, and what evidence supports that comparison?

The goal is not to present similarity as certainty or correlation as causation. Results should remain inspectable, source-linked, and falsifiable.

## Tech stack

- Python
- Flask
- SQLite / FTS5
- NumPy
- Sentence Transformers
- PyTorch
- `all-MiniLM-L6-v2`
- HTML / CSS
- Optional Node.js tooling

## Project status

Current development focus is moving from message-level retrieval toward unsupervised recurring-event discovery using exchange-level representations while preserving user/assistant provenance.

## License / dataset note

The repository does not include private conversation data. Public demo data can be generated from `HuggingFaceH4/ultrachat_200k` using `import_ultrachat.py`. Review the upstream dataset terms before redistributing derived data.
