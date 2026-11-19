#!/usr/bin/env python3
"""Inspect the archive without printing message contents or modifying its database."""
from __future__ import annotations

import argparse
from pathlib import Path
import importlib.util
import sqlite3

from storage import get_database_path


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--db', type=Path, help='Archive SQLite path; same default as Archive Explorer')
    args = parser.parse_args()
    database = args.db if args.db else get_database_path()
    print(f'Archive database: {database}')
    print('This checks the Python archive, not the separate Lumina analyzer.db.')
    for module in ('flask', 'numpy', 'sentence_transformers', 'torch'):
        print(f'{module}: {"installed" if importlib.util.find_spec(module) else "missing"}')
    if not database.is_file():
        print('Database missing. Import a conversations.json export before indexing.')
        return 1
    try:
        conn = sqlite3.connect(database.resolve().as_uri() + '?mode=ro', uri=True)
        try:
            tables = {row[0] for row in conn.execute("SELECT name FROM sqlite_master WHERE type='table'")}
            if 'messages' not in tables:
                print('This is not an Archive Explorer message database.')
                return 1
            print(f"Messages: {conn.execute('SELECT count(*) FROM messages').fetchone()[0]}")
            print(f"Keyword index: {'present' if 'message_search' in tables else 'missing; LIKE fallback'}")
            for role, count in conn.execute('SELECT role, count(*) FROM messages GROUP BY role'):
                print(f'Role {role}: {count}')
            if 'message_embeddings' not in tables:
                print('Semantic index: missing')
            else:
                for model, dimension, count in conn.execute('''
                    SELECT me.model_name, me.dim, count(*)
                    FROM message_embeddings me JOIN messages m ON m.message_id=me.message_id
                    GROUP BY me.model_name, me.dim ORDER BY me.model_name, me.dim
                '''):
                    print(f'Embedding model: {model}; dimensions={dimension}; rows={count}')
                print('Counts show stored rows, not proof of vector validity or search quality.')
            return 0
        finally:
            conn.close()
    except sqlite3.Error as exc:
        print(f'Database check failed: {exc}')
        return 1


if __name__ == '__main__':
    raise SystemExit(main())
