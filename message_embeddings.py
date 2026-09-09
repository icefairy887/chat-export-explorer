from __future__ import annotations

import hashlib
import sqlite3
from datetime import datetime, timezone
from functools import lru_cache

import numpy as np

MODEL_NAME = "all-MiniLM-L6-v2"
BATCH_SIZE = 32


@lru_cache(maxsize=1)
def get_encoder(model_name: str = MODEL_NAME):
    """Keep one encoder resident; never silently substitute another model."""
    try:
        from sentence_transformers import SentenceTransformer
    except ImportError as exc:
        raise RuntimeError(
            "Semantic dependencies are missing. Run: python -m pip install -r requirements-semantic.txt"
        ) from exc
    try:
        return SentenceTransformer(model_name, trust_remote_code=False,
                                   model_kwargs={"torch_dtype": "auto"})
    except Exception as exc:
        raise RuntimeError(f"Could not load embedding model {model_name}: {exc}") from exc


def encode_texts(encoder, texts, *, query=False, batch_size=BATCH_SIZE):
    # Model-defined retrieval prompts are applied only to the appropriate side.
    method = encoder.encode_query if query else encoder.encode_document
    vectors = np.asarray(method(
        texts, batch_size=batch_size, convert_to_numpy=True,
        normalize_embeddings=True, show_progress_bar=False,
    ), dtype=np.float32)
    if vectors.ndim != 2 or vectors.shape[0] != len(texts) or vectors.shape[1] == 0:
        raise ValueError("Encoder returned an unexpected vector shape")
    norms = np.linalg.norm(vectors, axis=1)
    if not np.isfinite(vectors).all() or not np.isfinite(norms).all() or np.any(norms <= 0):
        raise ValueError("Encoder returned a zero or non-finite vector")
    return vectors


def text_hash(text: str) -> str:
    return hashlib.sha256(text.encode("utf-8")).hexdigest()


def ensure_message_embedding_schema(conn: sqlite3.Connection) -> None:
    """Atomically preserve legacy vectors while allowing models to coexist."""
    conn.execute("SAVEPOINT embedding_schema")
    try:
        columns = conn.execute("PRAGMA table_info(message_embeddings)").fetchall()
        if columns:
            primary_key = [r[1] for r in sorted(columns, key=lambda r: r[5]) if r[5]]
            if primary_key == ["message_id"]:
                conn.execute("ALTER TABLE message_embeddings RENAME TO message_embeddings_legacy")
                conn.execute("DROP INDEX IF EXISTS idx_message_embeddings_model")
        conn.execute("""
            CREATE TABLE IF NOT EXISTS message_embeddings (
                message_id TEXT NOT NULL,
                embedding BLOB, dim INTEGER, norm REAL,
                model_name TEXT NOT NULL, created_at TEXT, text_hash TEXT,
                PRIMARY KEY (message_id, model_name)
            )
        """)
        if columns and primary_key == ["message_id"]:
            conn.execute("""
                INSERT INTO message_embeddings
                    (message_id, embedding, dim, norm, model_name, created_at)
                SELECT message_id, embedding, dim, norm, COALESCE(model_name, ?), created_at
                FROM message_embeddings_legacy
            """, (MODEL_NAME,))
            conn.execute("DROP TABLE message_embeddings_legacy")
        elif columns and "text_hash" not in {r[1] for r in columns}:
            conn.execute("ALTER TABLE message_embeddings ADD COLUMN text_hash TEXT")
        conn.execute("CREATE INDEX IF NOT EXISTS idx_message_embeddings_model ON message_embeddings(model_name)")
        conn.execute("RELEASE embedding_schema")
    except Exception:
        conn.execute("ROLLBACK TO embedding_schema")
        conn.execute("RELEASE embedding_schema")
        raise


def build_message_embeddings(conn: sqlite3.Connection, batch_size: int = BATCH_SIZE,
                             *, model_name: str = MODEL_NAME):
    """Incremental build; changed texts are re-encoded. Other models stay intact."""
    if batch_size < 1:
        raise ValueError("batch_size must be positive")
    ensure_message_embedding_schema(conn)
    rows = conn.execute("""
        SELECT m.message_id, m.text, me.text_hash
        FROM messages m LEFT JOIN message_embeddings me
          ON m.message_id = me.message_id AND me.model_name = ?
        WHERE trim(m.text) <> '' ORDER BY m.created_at, m.message_id
    """, (model_name,)).fetchall()
    pending = [(mid, text, text_hash(text)) for mid, text, old_hash in rows
               if old_hash != text_hash(text)]
    already = len(rows) - len(pending)
    if not pending:
        return len(rows), already, 0, []
    encoder = get_encoder(model_name)
    inserted, failures = 0, []
    for start in range(0, len(pending), batch_size):
        batch = pending[start:start + batch_size]
        try:
            vectors = encode_texts(encoder, [r[1] for r in batch], batch_size=batch_size)
            now = datetime.now(timezone.utc).isoformat()
            values = [(mid, vector.tobytes(), vector.size, float(np.linalg.norm(vector)),
                       model_name, now, digest)
                      for (mid, _, digest), vector in zip(batch, vectors)]
            conn.execute("SAVEPOINT embedding_batch")
            try:
                conn.executemany("""
                    INSERT INTO message_embeddings
                        (message_id, embedding, dim, norm, model_name, created_at, text_hash)
                    VALUES (?, ?, ?, ?, ?, ?, ?)
                    ON CONFLICT(message_id, model_name) DO UPDATE SET
                        embedding=excluded.embedding, dim=excluded.dim, norm=excluded.norm,
                        created_at=excluded.created_at, text_hash=excluded.text_hash
                """, values)
                conn.execute("RELEASE embedding_batch")
            except Exception:
                conn.execute("ROLLBACK TO embedding_batch")
                conn.execute("RELEASE embedding_batch")
                raise
            inserted += len(values)
        except Exception as exc:
            failures.extend((mid, str(exc)) for mid, _, _ in batch)
    return len(rows), already, inserted, failures


def semantic_message_search(conn: sqlite3.Connection, query: str, limit: int = 20,
                            *, model_name: str = MODEL_NAME, role: str | None = None):
    """Exact cosine search, filtered by model and speaker before ranking."""
    if not query.strip():
        raise ValueError("Enter a non-empty query")
    if limit < 1:
        raise ValueError("limit must be positive")
    if role not in (None, "", "user", "assistant", "system", "tool"):
        raise ValueError("Unknown message role")
    # Search is read-only: old indexes can be searched before an explicit rebuild.
    columns = conn.execute("PRAGMA table_info(message_embeddings)").fetchall()
    if not columns:
        raise RuntimeError("No semantic index exists. Build embeddings for this database first.")
    hash_column = "me.text_hash" if "text_hash" in {r[1] for r in columns} else "NULL"
    sql = f"""
        SELECT me.message_id, me.embedding, me.dim, m.conversation_id,
               m.created_at, m.text, m.role, {hash_column}
        FROM message_embeddings me JOIN messages m ON me.message_id = m.message_id
        WHERE me.model_name = ? AND trim(m.text) <> ''
    """
    params = [model_name]
    if role:
        sql += " AND m.role = ?"
        params.append(role)
    rows = conn.execute(sql, params).fetchall()
    if not rows:
        raise RuntimeError(f"No indexed messages for model {model_name} and the selected speaker.")
    query_vector = encode_texts(get_encoder(model_name), [query], query=True)[0]
    query_vector = query_vector / np.linalg.norm(query_vector)
    results = []
    for mid, blob, dim, conversation_id, created_at, text, speaker, digest in rows:
        if digest is not None and digest != text_hash(text):
            continue  # Never return an old vector for edited text.
        try:
            vector = np.frombuffer(blob, dtype=np.float32)
            if vector.size != dim or vector.size != query_vector.size or not np.isfinite(vector).all():
                continue
            norm = np.linalg.norm(vector)
            if not np.isfinite(norm) or norm <= 0:
                continue
            similarity = float(np.clip(np.dot(query_vector, vector / norm), -1, 1))
        except (TypeError, ValueError):
            continue
        results.append(dict(message_id=mid, conversation_id=conversation_id,
                            created_at=created_at, text=text, role=speaker, similarity=similarity))
    if not results:
        raise RuntimeError("No valid vectors remain. Rebuild the selected model's index.")
    return sorted(results, key=lambda r: (-r["similarity"], r["message_id"]))[:limit]
