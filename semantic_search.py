#!/usr/bin/env python3
from __future__ import annotations

import argparse
import sqlite3
import sys
from message_embeddings import build_message_embeddings, semantic_message_search


def main():
    parser = argparse.ArgumentParser(description='Semantic message search CLI')
    parser.add_argument('query', nargs='*', help='Query text (if omitted, reads from stdin)')
    parser.add_argument('--db', default='chat_history.db', help='Path to SQLite DB')
    parser.add_argument('--rebuild', action='store_true', help='(Re)build message embeddings incrementally before search')
    parser.add_argument('--limit', type=int, default=20, help='Number of results to show')
    args = parser.parse_args()

    if args.query:
        query = ' '.join(args.query)
    else:
        query = sys.stdin.read().strip()

    conn = sqlite3.connect(args.db)
    conn.row_factory = sqlite3.Row

    if args.rebuild:
        print('Starting incremental embedding build...')
        total, already, new, failures = build_message_embeddings(conn)
        print(f'Build complete: total={total}, already={already}, new={new}, failures={len(failures)}')
        if failures:
            print('Sample failures:')
            for f in failures[:10]:
                print(f)

    results = semantic_message_search(conn, query, limit=args.limit)
    print(f"Top {len(results)} matches for: {query}\n")
    for i, r in enumerate(results, 1):
        print(f"{i}. similarity={r['similarity']:.4f}  message_id={r['message_id']}  conversation_id={r['conversation_id']}  created_at={r['created_at']}")
        print(r['text'])
        print('-' * 80)


if __name__ == '__main__':
    main()
