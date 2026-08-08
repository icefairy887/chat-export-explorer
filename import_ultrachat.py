#!/usr/bin/env python3
import sqlite3
import argparse
import os
from datetime import datetime, timedelta
import hashlib

try:
    from datasets import load_dataset
except Exception as e:
    print('datasets library is required. Install with `pip install datasets`')
    raise

ROLE_MAP = {
    'human': 'user',
    'user': 'user',
    'assistant': 'assistant',
    'gpt': 'assistant',
    'bot': 'assistant',
}

BASE_TIME = datetime(2020,1,1,0,0,0)


def ensure_schema(conn):
    cur=conn.cursor()
    cur.execute('''
    CREATE TABLE IF NOT EXISTS conversations (
        conversation_id TEXT PRIMARY KEY,
        title TEXT,
        created_at TEXT,
        updated_at TEXT
    )
    ''')
    cur.execute('''
    CREATE TABLE IF NOT EXISTS messages (
        message_id TEXT PRIMARY KEY,
        conversation_id TEXT NOT NULL,
        parent_id TEXT,
        role TEXT,
        created_at TEXT,
        text TEXT NOT NULL,
        metadata_json TEXT,
        FOREIGN KEY (conversation_id) REFERENCES conversations(conversation_id)
    )
    ''')
    conn.commit()


def detect_message_list(example):
    # Return (field_name, list) or (None, None)
    for k,v in example.items():
        if isinstance(v, list) and len(v)>0 and isinstance(v[0], (dict,)):
            # check for fields suggestive of messages
            keys = set(v[0].keys())
            if keys & {'from','role','speaker'} and keys & {'value','text','content'}:
                return k, v
    # fallback: check any list of strings? Some datasets store list of turns as strings alternating role indicators.
    for k,v in example.items():
        if isinstance(v, list) and len(v)>0 and isinstance(v[0], str):
            # can't be sure, skip
            pass
    return None, None


def normalize_turn(turn):
    # turn is dict; try to extract role and text
    role=None
    text=None
    if 'from' in turn:
        role = ROLE_MAP.get(turn.get('from').lower(), None) if isinstance(turn.get('from'), str) else None
    if not role and 'role' in turn:
        role = ROLE_MAP.get(turn.get('role').lower(), None) if isinstance(turn.get('role'), str) else None
    if not role and 'speaker' in turn:
        role = ROLE_MAP.get(turn.get('speaker').lower(), None) if isinstance(turn.get('speaker'), str) else None
    # text fields
    for f in ('value','text','content','utterance'):
        if f in turn and isinstance(turn[f], str):
            text = turn[f]
            break
    # sometimes content is in nested 'message' or 'message/content'
    if text is None:
        # try to find first string field
        for k,v in turn.items():
            if isinstance(v, str) and len(v)>0:
                text = v
                break
    return role, text


def synthetic_timestamp(conv_idx, msg_idx):
    # deterministic timestamp: base + conv_idx days + msg_idx minutes
    ts = BASE_TIME + timedelta(days=conv_idx, minutes=msg_idx)
    return ts.isoformat() + '+00:00'


def make_id(prefix, conv_idx, msg_idx=None, text=None):
    if msg_idx is None:
        # conversation id
        s = f'{prefix}-{conv_idx}'
        return s
    else:
        # message id deterministic: hash of conv_idx, msg_idx, and first 40 chars of text
        txt = (text or '')[:120]
        h = hashlib.sha1(f'{conv_idx}-{msg_idx}-{txt}'.encode('utf8')).hexdigest()[:16]
        return f'{prefix}-{conv_idx}-{msg_idx}-{h}'


def import_ultrachat(limit, dbpath):
    print('Loading HuggingFaceH4/ultrachat_200k dataset (this may download ~GBs)')
    ds = load_dataset('HuggingFaceH4/ultrachat_200k')
    # dataset may have splits; use 'train' or first split
    split = 'train' if 'train' in ds else list(ds.keys())[0]
    dataset = ds[split]
    print('Dataset split:', split)

    conn = sqlite3.connect(dbpath)
    ensure_schema(conn)
    cur = conn.cursor()

    imported_conversations = 0
    imported_messages = 0
    user_messages = 0
    assistant_messages = 0
    malformed = 0

    # iterate deterministically: dataset is an IterableDataset or ArrowDataset; use .select(range(limit)) if possible
    # Some datasets may be row-wise conversation entries
    it = dataset
    try:
        total = min(limit, len(dataset))
        iterator = (dataset[i] for i in range(total))
    except Exception:
        # fallback to streaming
        iterator = dataset.take(limit)

    for conv_idx, example in enumerate(iterator):
        if conv_idx >= limit:
            break
        # detect messages list
        field, msgs = detect_message_list(example)
        if msgs is None:
            # try common schemas: example may itself be a dict with 'turns'
            if isinstance(example, dict) and ('conversations' in example and isinstance(example['conversations'], list)):
                msgs = example['conversations']
            else:
                malformed += 1
                continue
        # create deterministic conversation id
        conv_id = make_id('ultrachat', conv_idx)
        title = None
        created_at = synthetic_timestamp(conv_idx, 0)
        updated_at = created_at
        # insert conversation idempotent
        cur.execute('INSERT OR IGNORE INTO conversations(conversation_id, title, created_at, updated_at) VALUES (?,?,?,?)', (conv_id, title, created_at, updated_at))
        conv_msg_count = 0
        for msg_idx, turn in enumerate(msgs):
            role, text = normalize_turn(turn)
            if role is None or text is None:
                # try to skip empty or malformed
                malformed += 1
                continue
            role = role if role in ('user','assistant') else ('user' if 'human' in role else 'assistant')
            ts = synthetic_timestamp(conv_idx, msg_idx+1)
            message_id = make_id('ultrachat_msg', conv_idx, msg_idx, text)
            parent_id = None
            metadata_json = None
            # insert idempotent
            cur.execute('INSERT OR IGNORE INTO messages(message_id, conversation_id, parent_id, role, created_at, text, metadata_json) VALUES (?,?,?,?,?,?,?)', (message_id, conv_id, parent_id, role, ts, text, metadata_json))
            # count inserts: check if row exists now
            if cur.rowcount>0:
                imported_messages += 1
                conv_msg_count += 1
                if role=='user':
                    user_messages += 1
                else:
                    assistant_messages += 1
        if conv_msg_count>0:
            imported_conversations += 1
        # commit periodically
        if conv_idx % 50 == 0:
            conn.commit()
    conn.commit()

    # sanity checks
    cur.execute('SELECT COUNT(*) FROM conversations')
    convs_total = cur.fetchone()[0]
    cur.execute('SELECT COUNT(*) FROM messages')
    msgs_total = cur.fetchone()[0]
    print('\nIMPORT COMPLETE')
    print(' conversations imported (new total in DB):', convs_total)
    print(' messages imported (new total in DB):', msgs_total)
    print(' user messages imported (this run):', user_messages)
    print(' assistant messages imported (this run):', assistant_messages)
    print(' malformed/skipped entries:', malformed)

    conn.close()


if __name__=='__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('--limit', type=int, default=1000, help='Max conversations to import')
    parser.add_argument('--database', type=str, default='demo_chat_history.db', help='Output SQLite DB path')
    args = parser.parse_args()
    dbpath = args.database
    if os.path.exists(dbpath):
        print('Using existing database:', dbpath)
    else:
        print('Creating database:', dbpath)
    import_ultrachat(args.limit, dbpath)
