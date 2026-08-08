import sqlite3
import uuid
import time
from datetime import datetime, timezone
import numpy as np

EMBED_MODEL_NAME = 'all-MiniLM-L6-v2'


def ensure_schema(conn: sqlite3.Connection):
    cur = conn.cursor()
    cur.execute('''
    CREATE TABLE IF NOT EXISTS events (
        event_id TEXT PRIMARY KEY,
        conversation_id TEXT,
        start_time TEXT,
        end_time TEXT,
        message_count INTEGER,
        user_message_count INTEGER,
        assistant_message_count INTEGER,
        centroid BLOB,
        dim INTEGER,
        norm REAL,
        model_name TEXT,
        rep_user_message_id TEXT,
        rep_assistant_message_id TEXT,
        created_at TEXT
    )
    ''')
    cur.execute('''
    CREATE TABLE IF NOT EXISTS event_messages (
        event_id TEXT,
        message_id TEXT,
        seq_in_event INTEGER,
        role TEXT,
        PRIMARY KEY (event_id, message_id)
    )
    ''')
    conn.commit()


def load_message_embeddings(conn: sqlite3.Connection):
    cur = conn.cursor()
    cur.execute("SELECT message_id, embedding, dim, norm, model_name FROM message_embeddings WHERE model_name=?", (EMBED_MODEL_NAME,))
    embeddings = {}
    for mid, blob, dim, norm, mname in cur.fetchall():
        if blob is None:
            continue
        vec = np.frombuffer(blob, dtype=np.float32)
        embeddings[mid] = vec
    return embeddings


def parse_time(s: str):
    # ISO format with timezone
    return datetime.fromisoformat(s)


def cosine(a: np.ndarray, b: np.ndarray):
    if a is None or b is None:
        return -1.0
    na = np.linalg.norm(a)
    nb = np.linalg.norm(b)
    if na == 0 or nb == 0:
        return -1.0
    return float(np.dot(a, b) / (na * nb))


def _save_event(conn: sqlite3.Connection, event):
    cur = conn.cursor()
    cur.execute('''INSERT INTO events(event_id, conversation_id, start_time, end_time, message_count, user_message_count, assistant_message_count, centroid, dim, norm, model_name, rep_user_message_id, rep_assistant_message_id, created_at)
                   VALUES (?,?,?,?,?,?,?,?,?,?,?,?,?,?)''', (
        event['event_id'], event['conversation_id'], event['start_time'], event['end_time'], event['message_count'], event['user_message_count'], event['assistant_message_count'],
        event['centroid'].astype(np.float32).tobytes(), event['dim'], float(np.linalg.norm(event['centroid'])), EMBED_MODEL_NAME, event.get('rep_user_message_id'), event.get('rep_assistant_message_id'), datetime.now(timezone.utc).isoformat()
    ))
    for seq, (mid, role) in enumerate(event['messages']):
        cur.execute('INSERT INTO event_messages(event_id, message_id, seq_in_event, role) VALUES (?,?,?,?)', (event['event_id'], mid, seq, role))
    conn.commit()


def segment_all(conn: sqlite3.Connection, sim_threshold: float = 0.6):
    """
    Segment every conversation into events using semantic + temporal rules.
    Rules per Phase 2:
      - conversation boundary = hard boundary
      - semantic discontinuity = primary boundary
      - 6-hour gap = strong boundary
      - 24-hour gap = absolute boundary
    """
    ensure_schema(conn)
    embeddings = load_message_embeddings(conn)
    cur = conn.cursor()
    # clear existing events for a fresh run
    cur.execute('DELETE FROM event_messages')
    cur.execute('DELETE FROM events')
    conn.commit()

    # iterate conversations
    cur.execute('SELECT conversation_id FROM messages GROUP BY conversation_id')
    convs = [r[0] for r in cur.fetchall()]
    total_events = 0
    for ci, conv in enumerate(convs, 1):
        cur.execute('SELECT message_id, role, created_at FROM messages WHERE conversation_id=? ORDER BY created_at', (conv,))
        rows = cur.fetchall()
        if not rows:
            continue
        # process conversation
        current = {
            'event_id': str(uuid.uuid4()),
            'conversation_id': conv,
            'start_time': None,
            'end_time': None,
            'messages': [],  # list of (message_id, role)
            'centroid_sum': None,
            'dim': None,
            'message_count': 0,
            'user_message_count': 0,
            'assistant_message_count': 0
        }
        last_time = None
        for (mid, role, created_at) in rows:
            msg_time = parse_time(created_at)
            vec = embeddings.get(mid)
            if current['message_count'] == 0:
                # start new event
                current['start_time'] = created_at
                current['end_time'] = created_at
                current['messages'].append((mid, role))
                if vec is not None:
                    current['centroid_sum'] = vec.astype(np.float64).copy()
                    current['dim'] = vec.shape[0]
                current['message_count'] = 1
                if role == 'user':
                    current['user_message_count'] = 1
                    current['assistant_message_count'] = 0
                else:
                    current['assistant_message_count'] = 1
                    current['user_message_count'] = 0
                last_time = msg_time
                continue
            # compute gap
            gap = (msg_time - last_time).total_seconds()
            # absolute 24h boundary
            if gap >= 24 * 3600:
                # finalize current
                current['end_time'] = (rows[rows.index((mid, role, created_at)) - 1][2]) if False else current['end_time']
                _finalize_and_save(conn, current, embeddings)
                total_events += 1
                # start new
                current = {
                    'event_id': str(uuid.uuid4()),
                    'conversation_id': conv,
                    'start_time': created_at,
                    'end_time': created_at,
                    'messages': [(mid, role)],
                    'centroid_sum': None,
                    'dim': None,
                    'message_count': 1,
                    'user_message_count': 1 if role == 'user' else 0,
                    'assistant_message_count': 1 if role != 'user' else 0
                }
                if embeddings.get(mid) is not None:
                    current['centroid_sum'] = embeddings[mid].astype(np.float64).copy()
                    current['dim'] = embeddings[mid].shape[0]
                last_time = msg_time
                continue
            # otherwise, semantic check
            if current['centroid_sum'] is None or embeddings.get(mid) is None:
                sim = -1.0
            else:
                centroid = (current['centroid_sum'] / current['message_count']).astype(np.float32)
                sim = cosine(centroid, embeddings[mid])
            if sim >= sim_threshold:
                # append to current
                current['messages'].append((mid, role))
                # update centroid sum
                if embeddings.get(mid) is not None:
                    if current['centroid_sum'] is None:
                        current['centroid_sum'] = embeddings[mid].astype(np.float64).copy()
                        current['dim'] = embeddings[mid].shape[0]
                    else:
                        current['centroid_sum'] += embeddings[mid].astype(np.float64)
                current['message_count'] += 1
                if role == 'user':
                    current['user_message_count'] += 1
                else:
                    current['assistant_message_count'] += 1
                current['end_time'] = created_at
                last_time = msg_time
                continue
            else:
                # semantic discontinuity
                # strong 6-hour boundary: if gap >= 6h, split; if gap <6h but semantically discontinuous, split as well
                if gap >= 6 * 3600:
                    # finalize and start new
                    _finalize_and_save(conn, current, embeddings)
                    total_events += 1
                    current = {
                        'event_id': str(uuid.uuid4()),
                        'conversation_id': conv,
                        'start_time': created_at,
                        'end_time': created_at,
                        'messages': [(mid, role)],
                        'centroid_sum': None,
                        'dim': None,
                        'message_count': 1,
                        'user_message_count': 1 if role == 'user' else 0,
                        'assistant_message_count': 1 if role != 'user' else 0
                    }
                    if embeddings.get(mid) is not None:
                        current['centroid_sum'] = embeddings[mid].astype(np.float64).copy()
                        current['dim'] = embeddings[mid].shape[0]
                    last_time = msg_time
                    continue
                else:
                    # gap <6h but semantically discontinuous -> finalize and start new
                    _finalize_and_save(conn, current, embeddings)
                    total_events += 1
                    current = {
                        'event_id': str(uuid.uuid4()),
                        'conversation_id': conv,
                        'start_time': created_at,
                        'end_time': created_at,
                        'messages': [(mid, role)],
                        'centroid_sum': None,
                        'dim': None,
                        'message_count': 1,
                        'user_message_count': 1 if role == 'user' else 0,
                        'assistant_message_count': 1 if role != 'user' else 0
                    }
                    if embeddings.get(mid) is not None:
                        current['centroid_sum'] = embeddings[mid].astype(np.float64).copy()
                        current['dim'] = embeddings[mid].shape[0]
                    last_time = msg_time
                    continue
        # end conversation -> finalize trailing
        if current['message_count'] > 0:
            _finalize_and_save(conn, current, embeddings)
            total_events += 1
    return total_events


def _finalize_and_save(conn, current, embeddings):
    # compute centroid
    if current['centroid_sum'] is None:
        centroid = np.zeros((1,), dtype=np.float32)
        dim = 0
    else:
        centroid = (current['centroid_sum'] / current['message_count']).astype(np.float32)
        dim = centroid.shape[0]
    # pick representative user and assistant messages if available: nearest to centroid among that role
    rep_user = None
    rep_assist = None
    best_user_sim = -2.0
    best_assist_sim = -2.0
    for mid, role in current['messages']:
        vec = embeddings.get(mid)
        if vec is None:
            continue
        sim = cosine(centroid, vec)
        if role == 'user' and sim > best_user_sim:
            best_user_sim = sim
            rep_user = mid
        if role != 'user' and sim > best_assist_sim:
            best_assist_sim = sim
            rep_assist = mid
    event = {
        'event_id': current['event_id'],
        'conversation_id': current['conversation_id'],
        'start_time': current['start_time'],
        'end_time': current['end_time'],
        'message_count': current['message_count'],
        'user_message_count': current.get('user_message_count', 0),
        'assistant_message_count': current.get('assistant_message_count', 0),
        'centroid': centroid,
        'dim': dim,
        'rep_user_message_id': rep_user,
        'rep_assistant_message_id': rep_assist,
        'messages': current['messages']
    }
    _save_event(conn, event)

