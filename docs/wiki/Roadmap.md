# Roadmap

## Current capabilities

- SQLite archive import
- FTS5 full-text search
- conversation browsing
- phrase/pattern tracking
- daily trajectory states
- semantic day-level retrieval
- decision simulation
- message-level local embeddings
- semantic message search
- exchange construction with user/context centroids
- public UltraChat demo import

## Next milestone: recurring event discovery

The next major analysis layer should operate on exchanges rather than raw messages.

Planned constraints:

- use user-authored evidence as the primary clustering signal
- keep assistant-authored text as contextual evidence only
- preserve source message IDs, timestamps, and conversation IDs
- keep cluster descriptions grounded in representative user messages
- report noise/outliers rather than forcing every exchange into a cluster
- inspect clustering quality before adding prediction or cost scoring

## Later milestones

### Event transitions

Measure which recurring event types tend to occur before or after others, with support counts and traceable examples.

### Outcome linkage

Connect recurring sequences to downstream trajectory changes while clearly separating correlation from causation.

### Better demo experience

Add screenshots, example queries, and a preflight script for public demo data.

### CI expansion

The current smoke test validates syntax and core imports. Later CI can add a tiny generated fixture database and route-level tests without requiring private data or downloading large embedding models.
