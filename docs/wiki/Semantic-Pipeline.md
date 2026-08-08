# Semantic Pipeline

## Message embeddings

Every non-empty message can be encoded with the local Sentence Transformers model `all-MiniLM-L6-v2`.

Embeddings are stored persistently in SQLite as float32 BLOBs with their dimension, norm, model name, and creation timestamp.

This makes semantic search incremental: messages already embedded with the same model do not need to be recomputed.

## Semantic search

`semantic_message_search()` embeds a query locally and compares it with stored message embeddings using exact cosine similarity.

At the current corpus scale, exact scanning is simple and fast enough. An approximate nearest-neighbor index is intentionally deferred until scale makes it necessary.

## Why message-level search matters

Daily aggregation is useful for trajectories but destroys within-day detail. Message-level retrieval restores evidence resolution and allows queries to retrieve conceptually similar messages even when wording differs.

## Why messages are not events

An early segmentation experiment treated adjacent messages as candidate events using semantic similarity. That produced too many singletons because questions and answers can be conversationally coherent without having highly similar embeddings.

The project therefore introduced an exchange layer before event discovery.

## Exchange representation

An exchange stores two semantic representations:

- `user_centroid`: derived only from user-authored messages
- `context_centroid`: derived from the full user + assistant exchange

The user centroid is the safer representation for discovering recurring user-authored structure. The context centroid remains useful for reconstruction and contextual retrieval.

## Next layer

The planned next stage is unsupervised clustering over exchange-level user centroids, with every discovered cluster remaining traceable to the underlying user messages, conversation IDs, and timestamps.
