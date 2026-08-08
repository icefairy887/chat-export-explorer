# Architecture

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
        |        +--> exchange construction
        |                 |
        |                 +--> user centroid
        |                 +--> context centroid
        |
        +--> daily trajectory states
                 |
                 +--> historical similarity
                 +--> decision simulator
```

## Storage layer

SQLite stores conversations, messages, search indexes, embeddings, trajectory states, and exchange mappings. Source messages remain the canonical evidence layer.

## Retrieval layer

Two retrieval mechanisms coexist:

- SQLite FTS5 for exact/full-text search
- Sentence embeddings for semantic similarity

The semantic message layer uses `all-MiniLM-L6-v2` and stores float32 vectors as BLOBs.

## Daily trajectory layer

Messages are aggregated into daily states with interpretable feature scores. Semantic embeddings can retrieve historically similar states while the feature system remains available for explanation and downstream cost/outcome scoring.

## Exchange layer

Individual messages proved too small to serve as reliable event units. The exchange layer groups contiguous user turns with the assistant response-run that follows them.

Each exchange preserves:

- source message IDs
- roles and timestamps
- conversation ID
- a user-only centroid
- a full-context centroid
- a representative user message

The user-only centroid is intended to remain the primary representation for discovering user-authored recurring patterns.

## Web layer

Flask exposes the dashboard, search, browse, phrase patterns, trajectory comparison, simulator, and health routes.
