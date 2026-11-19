#!/usr/bin/env python3
"""Create a small synthetic archive and ChatGPT-shaped export for local exploration."""
import argparse
from datetime import datetime, timedelta, timezone
import json
from pathlib import Path

from evaluate_embeddings import DEFAULT_CASES, load_cases
from import_export import build_database


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--db', type=Path, default=Path('artifacts/search-demo.db'))
    args = parser.parse_args()
    export = args.db.with_name(args.db.stem + '-export.json')
    if args.db.exists() or export.exists():
        parser.error('Demo output already exists. Reuse it or choose a new --db path.')
    documents, _ = load_cases(DEFAULT_CASES)
    conversations = []
    for index, document in enumerate(documents):
        timestamp = (datetime(2026, 1, 1, tzinfo=timezone.utc) + timedelta(days=index)).timestamp()
        conversations.append(dict(
            id='demo-' + document['id'], title='Synthetic example ' + document['id'],
            create_time=timestamp, update_time=timestamp,
            mapping={document['id']: dict(parent=None, message=dict(
                id=document['id'], author=dict(role='user'), create_time=timestamp,
                content=dict(content_type='text', parts=[document['text']]), metadata={}))}))
    args.db.parent.mkdir(parents=True, exist_ok=True)
    export.write_text(json.dumps(conversations, indent=2), encoding='utf-8')
    _, count, messages = build_database(export, args.db)
    print(f'Created {count} synthetic conversations and {messages} messages.')
    print(f'Archive: {args.db.resolve()}\nDesktop-compatible export: {export.resolve()}')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
