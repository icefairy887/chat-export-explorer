from __future__ import annotations

import sqlite3
import time
from datetime import datetime
from typing import List, Tuple

import numpy as np

MODEL_NAME = "all-MiniLM-L6-v2"
BATCH_SIZE = 256


def get_encoder():
    try:
        from sentence_transformers import SentenceTransformer
    except Exception:
        return None
    try:
        return SentenceTransformer(MODEL_NAME)
    except Exception:
        return None


def ensure_message_embedding_schema(conn: sqlite3.Connection) -> None:
    conn.executescript(
        """
        CREATE TABLE IF NOT EXISTS message_embeddings (
            message_id TEXT PRIMARY KEY,
            embedding BLOB,
            dim INTEGER,
            norm REAL,
            model_name TEXT,
            created_at TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_message_embeddings_model ON message_embeddings(model_name);
        """
    )


def _to_blob(emb: np.ndarray) -> bytes:
    # ensure float32
    arr = np.array(emb, dtype=np.float32, copy=False)
    return arr.tobytes()


def _from_blob(blob: bytes, dim: int) -> np.ndarray:
    return np.frombuffer(blob, dtype=np.float32).reshape((dim,))


def build_message_embeddings(conn: sqlite3.Connection, batch_size: int = BATCH_SIZE) -> Tuple[int, int, int, List[Tuple[str, str]]]:
    """
    Incrementally build message embeddings for every non-empty message in the messages table.
    Returns: (total_eligible, already_embedded, newly_embedded, failures)
    failures is a list of (message_id, reason)
    """
    ensure_message_embedding_schema(conn)
    cur = conn.cursor()

    # Count eligible messages
    cur.execute("SELECT COUNT(*) FROM messages WHERE trim(text) <> ''")
    total_eligible = cur.fetchone()[0]

    # Find already embedded message_ids for this model
    cur.execute("SELECT message_id FROM message_embeddings WHERE model_name = ?", (MODEL_NAME,))
    already = {row[0] for row in cur.fetchall()}
    already_count = len(already)

    remaining_count = total_eligible - already_count

    print(f"Total eligible messages: {total_eligible}")
    print(f"Already embedded (model={MODEL_NAME}): {already_count}")
    print(f"Remaining: {max(0, remaining_count)}")

    # Query all messages (id and text) that need embeddings
    cur.execute("SELECT message_id, text FROM messages WHERE trim(text) <> '' ORDER BY created_at")
    rows = cur.fetchall()

    # Filter rows to those not already embedded
    to_process = [(r[0], r[1]) for r in rows if r[0] not in already]
    total_to_process = len(to_process)

    if total_to_process == 0:
        print("No messages to embed.")
        return total_eligible, already_count, 0, []

    enc = get_encoder()
    if enc is None:
        print("Local encoder not available. Install sentence-transformers to enable embeddings.")
        return total_eligible, already_count, 0, [(m[0], "encoder_missing") for m in to_process]

    failures: List[Tuple[str, str]] = []
    inserted = 0

    # Process in batches
    start_time = time.time()
    for i in range(0, total_to_process, batch_size):
        batch = to_process[i : i + batch_size]
        ids = [m[0] for m in batch]
        texts = [m[1] for m in batch]
        print(f"Processing batch {i//batch_size + 1} - messages {i + 1}..{i + len(batch)} of {total_to_process}")
        try:
            embs = enc.encode(texts, show_progress_bar=True, convert_to_numpy=True)
        except Exception as e:
            # Record failures for this batch
            for mid in ids:
                failures.append((mid, f"encode_error: {e}"))
            continue

        # Prepare insert rows
        insert_rows = []
        ts = datetime.utcnow().isoformat() + "Z"
        for mid, emb in zip(ids, embs):
            try:
                arr = np.array(emb, dtype=np.float32, copy=False)
                dim = int(arr.shape[0])
                norm = float(np.linalg.norm(arr))
                blob = arr.tobytes()
                insert_rows.append((mid, sqlite3.Binary(blob), dim, norm, MODEL_NAME, ts))
            except Exception as e:
                failures.append((mid, f"pack_error: {e}"))

        # Insert into DB
        try:
            cur.executemany(
                "INSERT OR REPLACE INTO message_embeddings (message_id, embedding, dim, norm, model_name, created_at) VALUES (?, ?, ?, ?, ?, ?)",
                insert_rows,
            )
            conn.commit()
            inserted += len(insert_rows)
        except Exception as e:
            # If commit fails, record failures
            for mid in ids:
                failures.append((mid, f"db_insert_error: {e}"))

        elapsed = time.time() - start_time
        avg_per = elapsed / (i + len(batch)) if (i + len(batch)) > 0 else 0
        remaining = total_to_process - (i + len(batch))
        eta = remaining * avg_per
        print(f"Batch done. Inserted so far: {inserted}. ETA: {int(eta)}s")

    print(f"Embedding complete. Newly embedded: {inserted}. Failures: {len(failures)}")
    return total_eligible, already_count, inserted, failures


def semantic_message_search(conn: sqlite3.Connection, query: str, limit: int = 20):
    """
    Embed the query locally and compare it against stored message embeddings.
    Returns top matches as list of dicts with keys: message_id, conversation_id, created_at, text, similarity
    """
    enc = get_encoder()
    if enc is None:
        raise RuntimeError("Local encoder not available")
    q_emb = enc.encode([query], convert_to_numpy=True)[0].astype(np.float32)
    q_norm = float(np.linalg.norm(q_emb))

    ensure_message_embedding_schema(conn)
    cur = conn.cursor()
    # Fetch embeddings joined with messages metadata
    cur.execute(
        "SELECT me.message_id, me.embedding, me.dim, me.norm, m.conversation_id, m.created_at, m.text "
        "FROM message_embeddings me JOIN messages m ON me.message_id = m.message_id "
        "WHERE me.model_name = ?",
        (MODEL_NAME,),
    )
    rows = cur.fetchall()

    sims = []
    for row in rows:
        mid = row[0]
        blob = row[1]
        dim = row[2]
        norm = row[3] or 0.0
        conv = row[4]
        created_at = row[5]
        text = row[6]
        try:
            emb = np.frombuffer(blob, dtype=np.float32)
            if emb.size != dim:
                # try reshaping
                emb = emb[:dim]
            dot = float(np.dot(q_emb, emb))
            denom = (q_norm * norm) if (q_norm and norm) else 1.0
            sim = dot / denom
        except Exception:
            sim = 0.0
        sims.append((sim, mid, conv, created_at, text))

    sims.sort(key=lambda x: x[0], reverse=True)
    results = []
    for sim, mid, conv, created_at, text in sims[:limit]:
        results.append({"message_id": mid, "conversation_id": conv, "created_at": created_at, "text": text, "similarity": sim})
    return results
