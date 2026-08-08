from __future__ import annotations

import math
import re
import sqlite3
from collections import Counter, defaultdict
from datetime import datetime, timedelta
from typing import Iterable

from trajectory import FEATURES, analyze_trajectory, cosine, dominant_features, load_states, score_text, vector, encode_texts

ACTION_PRESETS: dict[str, tuple[str, ...]] = {
    "Apply for jobs": ("apply", "applied", "application", "resume", "recruiter", "interview"),
    "Study or build": ("study", "studied", "course", "class", "project", "built", "coding", "certification"),
    "Send the message": ("texted", "messaged", "called", "emailed", "sent him", "sent her", "reached out"),
    "Wait and observe": ("wait", "waited", "didn't text", "did not text", "left it alone", "said nothing", "let it sit"),
    "Set a boundary": ("boundary", "told him", "told her", "not accepting", "won't accept", "said no", "stood firm"),
    "Leave or disengage": ("left", "leave", "blocked", "done", "walk away", "ended it", "broke up"),
    "Rest and reset": ("rest", "slept", "nap", "break", "shower", "cleaned", "reset", "went outside"),
}

POSITIVE_FEATURES = {"career", "learning", "action", "confidence"}
COST_FEATURES = {"distress", "uncertainty"}


def _dates_between(start: str, days: int) -> list[str]:
    base = datetime.fromisoformat(start)
    return [(base + timedelta(days=i)).date().isoformat() for i in range(days + 1)]


def _action_terms(label: str, custom_terms: str = "") -> tuple[str, ...]:
    if label in ACTION_PRESETS:
        return ACTION_PRESETS[label]
    terms = [x.strip().lower() for x in re.split(r"[,;\n]+", custom_terms) if x.strip()]
    return tuple(terms or [label.lower()])


def _action_seen(conn: sqlite3.Connection, state_date: str, terms: Iterable[str], action_window: int = 3) -> tuple[bool, list[dict]]:
    dates = _dates_between(state_date, action_window)
    placeholders = ",".join("?" for _ in dates)
    rows = conn.execute(
        f"""
        SELECT m.created_at, m.text, m.conversation_id, c.title
        FROM messages m
        JOIN conversations c ON c.conversation_id = m.conversation_id
        WHERE m.role='user' AND substr(m.created_at,1,10) IN ({placeholders})
        ORDER BY m.created_at
        """,
        dates,
    ).fetchall()
    hits = []
    lowered_terms = tuple(t.lower() for t in terms)
    for row in rows:
        lowered = row["text"].lower()
        matched = [term for term in lowered_terms if term in lowered]
        if matched:
            hits.append({
                "date": row["created_at"],
                "text": row["text"],
                "conversation_id": row["conversation_id"],
                "title": row["title"],
                "matched_terms": matched,
            })
    return bool(hits), hits[:4]


def _future_state(dated: dict[str, dict], start_date: str, horizon_days: int) -> dict | None:
    target = datetime.fromisoformat(start_date) + timedelta(days=horizon_days)
    for extra in range(0, 4):
        found = dated.get((target + timedelta(days=extra)).date().isoformat())
        if found:
            return found
    return None


def _branch_score(deltas: dict[str, float]) -> float:
    benefit = sum(deltas.get(name, 0.0) for name in POSITIVE_FEATURES)
    cost_reduction = -sum(deltas.get(name, 0.0) for name in COST_FEATURES)
    relationship_penalty = max(deltas.get("relationship", 0.0), 0.0) * 0.15
    return benefit + cost_reduction - relationship_penalty


def simulate_branches(
    conn: sqlite3.Connection,
    current_text: str,
    branches: list[dict[str, str]],
    horizon_days: int = 14,
    match_limit: int = 24,
) -> dict:
    states = load_states(conn)
    current_scores = score_text(current_text)
    current_vector = vector(current_scores)
    dated = {state["date"]: state for state in states}

    # Try semantic embedding for the current text and rank by embedding similarity
    current_embedding = None
    enc = encode_texts([current_text])
    if enc:
        current_embedding = enc[0]

    ranked_states = sorted(
        (
            {
                **state,
                "similarity": (
                    cosine(current_embedding, state.get("embedding"))
                    if current_embedding and state.get("embedding")
                    else cosine(current_vector, vector(state["features"]))
                ),
            }
            for state in states
        ),
        key=lambda item: item["similarity"],
        reverse=True,
    )[:match_limit]

    results = []
    for branch in branches:
        label = branch.get("label", "").strip()
        if not label:
            continue
        terms = _action_terms(label, branch.get("terms", ""))
        cases = []
        delta_values: dict[str, list[float]] = defaultdict(list)
        outcomes: Counter[str] = Counter()

        for state in ranked_states:
            acted, evidence = _action_seen(conn, state["date"], terms)
            if not acted:
                continue
            future = _future_state(dated, state["date"], horizon_days)
            if not future:
                continue
            deltas = {
                feature: future["features"].get(feature, 0.0) - state["features"].get(feature, 0.0)
                for feature in FEATURES
            }
            for feature, delta in deltas.items():
                delta_values[feature].append(delta)
            for feature, score in dominant_features(future["features"], 3):
                if score > 0.12:
                    outcomes[feature] += 1
            cases.append({
                "state": state,
                "future": future,
                "evidence": evidence,
                "deltas": deltas,
                "score": _branch_score(deltas),
            })

        average_deltas = {
            feature: sum(values) / len(values)
            for feature, values in delta_values.items() if values
        }
        valid = len(cases)
        outcome_list = [
            {"feature": feature, "count": count, "percent": round(count * 100 / valid)}
            for feature, count in outcomes.most_common(5)
        ] if valid else []
        score = _branch_score(average_deltas) if average_deltas else 0.0
        results.append({
            "label": label,
            "terms": terms,
            "cases": sorted(cases, key=lambda case: case["state"]["similarity"], reverse=True)[:6],
            "case_count": valid,
            "average_deltas": sorted(average_deltas.items(), key=lambda item: abs(item[1]), reverse=True)[:7],
            "outcomes": outcome_list,
            "score": score,
            "coverage": round(valid * 100 / max(len(ranked_states), 1)),
        })

    results.sort(key=lambda result: (result["case_count"] > 0, result["score"], result["case_count"]), reverse=True)
    return {
        "current_dominant": dominant_features(current_scores),
        "matched_state_count": len(ranked_states),
        "horizon_days": horizon_days,
        "branches": results,
        "best_supported": next((r for r in results if r["case_count"] >= 2), None),
    }
