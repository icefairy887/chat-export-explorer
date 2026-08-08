from __future__ import annotations

import json
import math
import re
import sqlite3
from collections import Counter, defaultdict
from dataclasses import dataclass
from datetime import datetime, timedelta, timezone
from pathlib import Path
from typing import Iterable, Optional

FEATURE_TERMS: dict[str, tuple[str, ...]] = {
    "career": ("job", "resume", "interview", "apply", "application", "career", "recruiter", "hiring", "work", "position"),
    "learning": ("study", "course", "class", "school", "college", "cert", "certification", "learn", "python", "azure", "powershell", "tableau", "coursera"),
    "relationship": ("relationship", "boyfriend", "girlfriend", "love", "text him", "text me", "break up", "leave him", "dexter", "ben", "partner"),
    "uncertainty": ("i don't know", "idk", "maybe", "should i", "what do i do", "i guess", "confused", "unsure", "what if"),
    "action": ("i applied", "i sent", "i did", "i finished", "i completed", "i started", "i signed up", "i called", "i emailed", "i submitted", "i'm going to"),
    "distress": ("panic", "spiral", "miserable", "cry", "scared", "anxious", "overwhelmed", "freak out", "can't calm", "upset", "angry"),
    "confidence": ("i can", "i got this", "confident", "proud", "excited", "good at", "qualified", "capable", "i know how"),
    "sleep": ("sleep", "slept", "awake", "alarm", "tired", "exhausted", "late login", "woke up"),
    "money": ("money", "paid", "pay", "broke", "rent", "bill", "refund", "credit", "salary", "hour", "financial"),
    "health": ("health", "doctor", "hospital", "pain", "sick", "medicine", "cancer", "period", "symptom"),
}

FEATURES = tuple(FEATURE_TERMS)
TOKEN_RE = re.compile(r"[a-z0-9']+")


def parse_iso(value: str | None) -> datetime | None:
    if not value:
        return None
    try:
        return datetime.fromisoformat(value.replace("Z", "+00:00"))
    except ValueError:
        return None


def score_text(text: str) -> dict[str, float]:
    lowered = text.lower()
    tokens = TOKEN_RE.findall(lowered)
    token_count = max(len(tokens), 1)
    scores: dict[str, float] = {}
    for feature, terms in FEATURE_TERMS.items():
        raw = sum(lowered.count(term) for term in terms)
        # Saturating normalization keeps long messages from dominating.
        density = raw / math.sqrt(token_count)
        scores[feature] = min(density, 3.0)
    scores["volume"] = min(math.log1p(token_count) / 5.0, 3.0)
    scores["questioning"] = min(text.count("?") / 3.0, 3.0)
    return scores


def vector(scores: dict[str, float]) -> list[float]:
    return [scores.get(name, 0.0) for name in (*FEATURES, "volume", "questioning")]


def cosine(a: list[float], b: list[float]) -> float:
    dot = sum(x * y for x, y in zip(a, b))
    na = math.sqrt(sum(x * x for x in a))
    nb = math.sqrt(sum(y * y for y in b))
    return dot / (na * nb) if na and nb else 0.0


# Embedding support (optional local sentence-transformers)
_ENCODER = None
_MODEL_NAME = "all-MiniLM-L6-v2"


def get_encoder():
    """Lazily load a local sentence-transformers encoder. Returns None if unavailable."""
    global _ENCODER
    if _ENCODER is not None:
        return _ENCODER
    try:
        from sentence_transformers import SentenceTransformer
    except Exception:
        return None
    try:
        print(f"Loading local embedding model '{_MODEL_NAME}'...")
        _ENCODER = SentenceTransformer(_MODEL_NAME)
        print("Model loaded.")
    except Exception:
        _ENCODER = None
    return _ENCODER


def encode_texts(texts: list[str]) -> Optional[list[list[float]]]:
    """Encode a list of texts into embedding vectors (lists of floats).
    Returns None if no local encoder is available.
    """
    enc = get_encoder()
    if enc is None:
        return None
    # sentence-transformers can accept a batch; show_progress_bar provides console progress.
    embs = enc.encode(texts, show_progress_bar=True)
    # Convert to regular Python lists
    try:
        return [list(map(float, e)) for e in embs]
    except Exception:
        # Fallback: try simple conversion
        return [list(e) for e in embs]


def ensure_schema(conn: sqlite3.Connection) -> None:
    # Create the table with embeddings_json if it doesn't exist. If the table exists
    # but lacks the embeddings_json column, ALTER TABLE to add it (SQLite allows
    # adding columns).
    conn.executescript(
        """
        CREATE TABLE IF NOT EXISTS trajectory_states (
            state_date TEXT PRIMARY KEY,
            message_count INTEGER NOT NULL,
            conversation_count INTEGER NOT NULL,
            text_sample TEXT NOT NULL,
            features_json TEXT NOT NULL,
            embeddings_json TEXT
        );
        CREATE INDEX IF NOT EXISTS idx_trajectory_state_date ON trajectory_states(state_date);
        """
    )
    # Ensure the embeddings_json column exists for older databases.
    cols = [row[1] for row in conn.execute("PRAGMA table_info('trajectory_states')")]
    if 'embeddings_json' not in cols:
        conn.execute("ALTER TABLE trajectory_states ADD COLUMN embeddings_json TEXT")


def build_states(conn: sqlite3.Connection) -> int:
    ensure_schema(conn)
    conn.execute("DELETE FROM trajectory_states")
    rows = conn.execute(
        """
        SELECT conversation_id, created_at, text
        FROM messages
        WHERE role='user' AND created_at IS NOT NULL AND trim(text) <> ''
        ORDER BY created_at
        """
    ).fetchall()
    grouped: dict[str, list[sqlite3.Row]] = defaultdict(list)
    for row in rows:
        dt = parse_iso(row["created_at"])
        if dt:
            grouped[dt.date().isoformat()].append(row)

    # Prepare batched encoding inputs so embedding generation can show progress
    days = sorted(grouped.items())
    combined_texts = ["\n".join(r["text"] for r in msgs) for _, msgs in days]
    embeddings = None
    if combined_texts:
        print(f"Preparing to compute embeddings for {len(combined_texts)} day(s)")
        embeddings = encode_texts(combined_texts)
        if embeddings is None:
            print("Local embedding model not available; continuing without embeddings.")
    inserts = []

    for i, (day, messages) in enumerate(days):
        combined = combined_texts[i]
        scores = score_text(combined)
        sample = "\n\n".join(row["text"][:600] for row in messages[:4])
        emb_json = None
        if embeddings:
            try:
                emb = embeddings[i]
                emb_json = json.dumps(emb, separators=(",", ":"))
            except Exception:
                emb_json = None
        inserts.append(
            (
                day,
                len(messages),
                len({row["conversation_id"] for row in messages}),
                sample,
                json.dumps(scores, separators=(",", ":")),
                emb_json,
            )
        )
        # Progress output for clarity
        if (i + 1) % 50 == 0 or (i + 1) == len(days):
            print(f"Built state {i+1}/{len(days)} ({day})")

    conn.executemany(
        "INSERT INTO trajectory_states (state_date, message_count, conversation_count, text_sample, features_json, embeddings_json) VALUES (?, ?, ?, ?, ?, ?)", inserts
    )
    conn.commit()
    return len(inserts)


def load_states(conn: sqlite3.Connection) -> list[dict]:
    ensure_schema(conn)
    rows = conn.execute(
        "SELECT * FROM trajectory_states ORDER BY state_date"
    ).fetchall()
    states = []
    for row in rows:
        emb = None
        if row["embeddings_json"]:
            try:
                emb = json.loads(row["embeddings_json"])
            except Exception:
                emb = None
        states.append(
            {
                "date": row["state_date"],
                "message_count": row["message_count"],
                "conversation_count": row["conversation_count"],
                "text_sample": row["text_sample"],
                "features": json.loads(row["features_json"]),
                "embedding": emb,
            }
        )
    return states


def dominant_features(scores: dict[str, float], limit: int = 5) -> list[tuple[str, float]]:
    return sorted(
        ((k, v) for k, v in scores.items() if k not in {"volume"}),
        key=lambda item: item[1], reverse=True,
    )[:limit]


def analyze_trajectory(conn: sqlite3.Connection, current_text: str, horizon_days: int = 14, limit: int = 8) -> dict:
    states = load_states(conn)
    if not states:
        build_states(conn)
        states = load_states(conn)

    current_scores = score_text(current_text)
    current_vector = vector(current_scores)

    # Try semantic embedding for the current text; fall back to feature vector if
    # embeddings are not available.
    current_embedding = None
    enc = encode_texts([current_text])
    if enc:
        current_embedding = enc[0]

    dated = {state["date"]: state for state in states}
    candidates = []

    for state in states:
        # Prefer semantic embedding similarity when both sides have embeddings.
        sim = 0.0
        if current_embedding is not None and state.get("embedding"):
            try:
                sim = cosine(current_embedding, state["embedding"])
            except Exception:
                sim = 0.0
        else:
            sim = cosine(current_vector, vector(state["features"]))
        if sim <= 0:
            continue
        future_date = (datetime.fromisoformat(state["date"]) + timedelta(days=horizon_days)).date().isoformat()
        future = dated.get(future_date)
        if future is None:
            # Use the first available state within a 3-day window after the horizon.
            for extra in range(1, 4):
                future = dated.get((datetime.fromisoformat(future_date) + timedelta(days=extra)).date().isoformat())
                if future:
                    break
        deltas = {}
        if future:
            for name in FEATURES:
                deltas[name] = future["features"].get(name, 0) - state["features"].get(name, 0)
        candidates.append({**state, "similarity": sim, "future": future, "future_dominant": dominant_features(future["features"], 3) if future else [], "deltas": deltas})

    matches = sorted(candidates, key=lambda x: x["similarity"], reverse=True)[:limit]
    outcome_counter: Counter[str] = Counter()
    avg_deltas: dict[str, list[float]] = defaultdict(list)
    for match in matches:
        if not match["future"]:
            continue
        for name, delta in match["deltas"].items():
            avg_deltas[name].append(delta)
        future_dom = dominant_features(match["future"]["features"], 2)
        for feature, score in future_dom:
            if score > 0.12:
                outcome_counter[feature] += 1

    outcomes = []
    denominator = sum(1 for m in matches if m["future"]) or 1
    for feature, count in outcome_counter.most_common(6):
        outcomes.append({"feature": feature, "count": count, "percent": round(count * 100 / denominator)})

    changes = []
    for feature, values in avg_deltas.items():
        if values:
            avg = sum(values) / len(values)
            changes.append((feature, avg))
    changes.sort(key=lambda x: abs(x[1]), reverse=True)

    return {
        "current_scores": current_scores,
        "current_dominant": dominant_features(current_scores),
        "matches": matches,
        "outcomes": outcomes,
        "changes": changes[:6],
        "horizon_days": horizon_days,
        "state_count": len(states),
    }
