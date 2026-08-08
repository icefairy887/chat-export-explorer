# Data and Privacy

Chat Export Explorer is designed so private conversation archives can remain local.

## Files that should never be committed

```text
chat_history.db
demo_chat_history.db
conversations.json
*.db
*.db-wal
*.db-shm
.env
```

The repository's `.gitignore` is intended to keep generated databases and private exports out of version control.

## Local embeddings

Message text is embedded locally with Sentence Transformers. The model itself may be downloaded from Hugging Face on first use, but conversation text is not sent to an external inference API by the project.

## Provenance separation

The project distinguishes between:

- **Observation evidence:** user-authored source messages
- **Interpretation/context:** assistant-authored messages

Assistant text may help preserve the surrounding conversational exchange, but should not be counted as independent evidence that a pattern attributed to the user exists.

## Public demo data

For screenshots, testing, and public demonstrations, use `import_ultrachat.py` to create a separate `demo_chat_history.db` from the public UltraChat dataset.

The demo database is generated locally and should also remain uncommitted because it is reproducible from the importer.
