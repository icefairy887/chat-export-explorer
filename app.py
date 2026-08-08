from __future__ import annotations

import os
import re
import sqlite3
from pathlib import Path
from flask import Flask, abort, g, redirect, render_template, request, url_for
from markupsafe import Markup, escape

BASE_DIR = Path(__file__).resolve().parent
DATABASE = Path(os.environ.get("CHAT_DB", BASE_DIR / "chat_history.db"))

app = Flask(__name__)
app.config["DATABASE"] = DATABASE


@app.template_filter("highlight_phrase")
def highlight_phrase(text: str, phrase: str) -> Markup:
    if not phrase:
        return Markup(escape(text))
    pattern = re.compile(re.escape(phrase), re.IGNORECASE)
    pieces = []
    last = 0
    for match in pattern.finditer(text):
        pieces.append(str(escape(text[last:match.start()])))
        pieces.append("<mark>" + str(escape(match.group(0))) + "</mark>")
        last = match.end()
    pieces.append(str(escape(text[last:])))
    return Markup("".join(pieces))


def get_db() -> sqlite3.Connection:
    if "db" not in g:
        if not DATABASE.exists():
            raise FileNotFoundError(f"Database not found: {DATABASE}")
        conn = sqlite3.connect(DATABASE)
        conn.row_factory = sqlite3.Row
        g.db = conn
    return g.db


@app.teardown_appcontext
def close_db(_: object | None = None) -> None:
    db = g.pop("db", None)
    if db is not None:
        db.close()


def safe_fts_query(query: str) -> str:
    query = query.strip()
    if not query:
        return ""
    # Preserve explicit FTS operators/quoted phrases. Otherwise quote each token
    # so punctuation and apostrophes do not break the query parser.
    if any(op in query.upper() for op in (" AND ", " OR ", " NOT ")) or '"' in query:
        return query
    tokens = re.findall(r"[\w']+", query, flags=re.UNICODE)
    return " AND ".join(f'"{token.replace(chr(34), chr(34)*2)}"' for token in tokens)


@app.errorhandler(FileNotFoundError)
def database_missing(exc: FileNotFoundError):
    return render_template("missing_database.html", error=str(exc)), 500


@app.route("/")
def index():
    db = get_db()
    stats = db.execute(
        """
        SELECT
          (SELECT COUNT(*) FROM conversations) AS conversations,
          (SELECT COUNT(*) FROM messages) AS messages,
          (SELECT COUNT(*) FROM messages WHERE role='user') AS user_messages,
          (SELECT COUNT(*) FROM messages WHERE role='assistant') AS assistant_messages,
          (SELECT COALESCE(SUM(length(text) - length(replace(text, ' ', '')) + 1), 0)
             FROM messages WHERE role='user' AND trim(text) <> '') AS user_words
        """
    ).fetchone()

    longest = db.execute(
        """
        SELECT c.conversation_id, c.title, c.created_at, COUNT(m.message_id) AS message_count
        FROM conversations c
        JOIN messages m ON m.conversation_id = c.conversation_id
        GROUP BY c.conversation_id, c.title, c.created_at
        ORDER BY message_count DESC
        LIMIT 12
        """
    ).fetchall()

    activity = db.execute(
        """
        SELECT substr(created_at, 1, 7) AS month, COUNT(*) AS message_count
        FROM messages
        WHERE created_at IS NOT NULL
        GROUP BY month
        ORDER BY month DESC
        LIMIT 18
        """
    ).fetchall()[::-1]

    return render_template("index.html", stats=stats, longest=longest, activity=activity)


@app.route("/search")
def search():
    query = request.args.get("q", "").strip()
    role = request.args.get("role", "").strip()
    page = max(request.args.get("page", 1, type=int), 1)
    per_page = 30
    offset = (page - 1) * per_page
    rows = []
    has_more = False

    if query:
        fts_query = safe_fts_query(query)
        sql = """
            SELECT message_id, conversation_id, title, role, created_at,
                   snippet(message_search, 5, '<mark>', '</mark>', ' … ', 28) AS snippet,
                   bm25(message_search) AS rank
            FROM message_search
            WHERE message_search MATCH ?
        """
        params: list[object] = [fts_query]
        if role in {"user", "assistant", "system", "tool"}:
            sql += " AND role = ?"
            params.append(role)
        sql += " ORDER BY rank, created_at DESC LIMIT ? OFFSET ?"
        params.extend([per_page + 1, offset])
        fetched = db_rows = get_db().execute(sql, params).fetchall()
        has_more = len(fetched) > per_page
        rows = fetched[:per_page]

    return render_template(
        "search.html", query=query, role=role, rows=rows, page=page, has_more=has_more
    )


@app.route("/conversation/<conversation_id>")
def conversation(conversation_id: str):
    db = get_db()
    convo = db.execute(
        "SELECT * FROM conversations WHERE conversation_id = ?", (conversation_id,)
    ).fetchone()
    if convo is None:
        abort(404)
    messages = db.execute(
        """
        SELECT message_id, role, created_at, text
        FROM messages
        WHERE conversation_id = ?
        ORDER BY CASE WHEN created_at IS NULL THEN 1 ELSE 0 END, created_at, rowid
        """,
        (conversation_id,),
    ).fetchall()
    return render_template("conversation.html", convo=convo, messages=messages)


@app.route("/browse")
def browse():
    db = get_db()
    page = max(request.args.get("page", 1, type=int), 1)
    per_page = 50
    rows = db.execute(
        """
        SELECT c.conversation_id, c.title, c.created_at, c.updated_at,
               COUNT(m.message_id) AS message_count
        FROM conversations c
        LEFT JOIN messages m ON m.conversation_id = c.conversation_id
        GROUP BY c.conversation_id, c.title, c.created_at, c.updated_at
        ORDER BY COALESCE(c.updated_at, c.created_at) DESC
        LIMIT ? OFFSET ?
        """,
        (per_page + 1, (page - 1) * per_page),
    ).fetchall()
    has_more = len(rows) > per_page
    return render_template("browse.html", rows=rows[:per_page], page=page, has_more=has_more)


@app.route("/patterns")
def patterns():
    phrase = request.args.get("phrase", "").strip()
    role = request.args.get("role", "user").strip()
    page = max(request.args.get("page", 1, type=int), 1)
    per_page = 30
    offset = (page - 1) * per_page

    summary = None
    monthly = []
    conversations = []
    rows = []
    has_more = False

    if phrase:
        db = get_db()
        where = "instr(lower(m.text), lower(?)) > 0"
        params: list[object] = [phrase]
        if role in {"user", "assistant", "system", "tool"}:
            where += " AND m.role = ?"
            params.append(role)

        summary = db.execute(
            f"""
            SELECT COUNT(*) AS occurrences,
                   COUNT(DISTINCT m.conversation_id) AS conversation_count,
                   MIN(m.created_at) AS first_seen,
                   MAX(m.created_at) AS latest_seen
            FROM messages m
            WHERE {where}
            """,
            params,
        ).fetchone()

        monthly = db.execute(
            f"""
            SELECT substr(m.created_at, 1, 7) AS month, COUNT(*) AS occurrence_count
            FROM messages m
            WHERE {where} AND m.created_at IS NOT NULL
            GROUP BY month
            ORDER BY month
            """,
            params,
        ).fetchall()

        conversations = db.execute(
            f"""
            SELECT c.conversation_id, c.title, MIN(m.created_at) AS first_match,
                   MAX(m.created_at) AS latest_match, COUNT(*) AS occurrence_count
            FROM messages m
            JOIN conversations c ON c.conversation_id = m.conversation_id
            WHERE {where}
            GROUP BY c.conversation_id, c.title
            ORDER BY occurrence_count DESC, latest_match DESC
            LIMIT 12
            """,
            params,
        ).fetchall()

        fetched = db.execute(
            f"""
            SELECT m.message_id, m.conversation_id, c.title, m.role, m.created_at, m.text
            FROM messages m
            JOIN conversations c ON c.conversation_id = m.conversation_id
            WHERE {where}
            ORDER BY m.created_at DESC, m.rowid DESC
            LIMIT ? OFFSET ?
            """,
            [*params, per_page + 1, offset],
        ).fetchall()
        has_more = len(fetched) > per_page
        rows = fetched[:per_page]

    return render_template(
        "patterns.html",
        phrase=phrase, role=role, summary=summary, monthly=monthly,
        conversations=conversations, rows=rows, page=page, has_more=has_more,
    )


@app.route('/simulate', methods=['GET', 'POST'])
def simulate_view():
    from trajectory import build_states
    from simulator import ACTION_PRESETS, simulate_branches

    db = get_db()
    exists = db.execute('SELECT COUNT(*) FROM sqlite_master WHERE type="table" AND name="trajectory_states"').fetchone()[0]
    if not exists or db.execute('SELECT COUNT(*) FROM trajectory_states').fetchone()[0] == 0:
        build_states(db)

    text = request.values.get('text', '').strip()
    horizon = request.values.get('horizon', 14, type=int) or 14
    horizon = horizon if horizon in {7, 14, 30} else 14
    selected = request.form.getlist('branches') if request.method == 'POST' else []
    custom_label = request.values.get('custom_label', '').strip()
    custom_terms = request.values.get('custom_terms', '').strip()
    analysis = None
    if text and selected:
        branches = [{'label': label, 'terms': ''} for label in selected]
        if custom_label and custom_label in selected:
            branches = [b for b in branches if b['label'] != custom_label]
            branches.append({'label': custom_label, 'terms': custom_terms})
        analysis = simulate_branches(db, text, branches, horizon_days=horizon)
    total_states = db.execute('SELECT COUNT(*) FROM trajectory_states').fetchone()[0]
    return render_template('simulate.html', text=text, horizon=horizon, selected=selected,
                           custom_label=custom_label, custom_terms=custom_terms,
                           presets=list(ACTION_PRESETS), analysis=analysis, total_states=total_states)

@app.route("/health")
def health():
    db = get_db()
    count = db.execute("SELECT COUNT(*) FROM messages").fetchone()[0]
    return {"ok": True, "database": str(DATABASE), "messages": count}

@app.route('/trajectory', methods=['GET', 'POST'])
def trajectory_view():
    from trajectory import analyze_trajectory, build_states, load_states

    text = request.values.get('text', '').strip()
    horizon = request.values.get('horizon', 14, type=int) or 14
    horizon = horizon if horizon in {7, 14, 30} else 14
    analysis = None
    db = get_db()
    state_count = db.execute('SELECT COUNT(*) FROM sqlite_master WHERE type="table" AND name="trajectory_states"').fetchone()[0]
    if not state_count:
        build_states(db)
    elif db.execute('SELECT COUNT(*) FROM trajectory_states').fetchone()[0] == 0:
        build_states(db)
    if text:
        analysis = analyze_trajectory(db, text, horizon_days=horizon)
    total_states = db.execute('SELECT COUNT(*) FROM trajectory_states').fetchone()[0]
    return render_template('trajectory.html', text=text, horizon=horizon, analysis=analysis, total_states=total_states)

if __name__ == "__main__":
    app.run(host="127.0.0.1", port=5000, debug=False)
