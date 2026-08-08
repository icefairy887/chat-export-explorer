# Chat Export Explorer Wiki

Chat Export Explorer is a local-first conversation analysis workspace built around SQLite, Flask, semantic embeddings, trajectory comparison, and evidence-preserving analysis.

The project is intentionally designed so that every higher-level result can be traced back to the original messages that produced it.

## Start here

- [Setup](Setup.md)
- [Architecture](Architecture.md)
- [Data and Privacy](Data-and-Privacy.md)
- [Semantic Pipeline](Semantic-Pipeline.md)
- [UltraChat Demo Data](UltraChat-Demo-Data.md)
- [Roadmap](Roadmap.md)

## Core ideas

### Retrieval and interpretation are separate

Semantic embeddings are used to retrieve historically similar material. Feature scoring, trajectory comparison, and downstream analysis are separate layers so similarity is not presented as proof or causation.

### User evidence stays distinguishable from assistant context

The exchange layer stores both a user-only centroid and a full-context centroid. This prevents assistant-authored interpretations from silently becoming independent evidence for patterns attributed to the user.

### Private archives stay local

Real databases and exported conversation files are excluded from Git. A public UltraChat importer provides a reproducible demo path without exposing private data.

## Current status

The current system supports full-text search, conversation browsing, phrase tracking, daily trajectory comparison, decision simulation, local semantic message search, and exchange construction. The next major analysis layer is unsupervised recurring-event discovery over exchange representations.
