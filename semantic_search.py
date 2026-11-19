#!/usr/bin/env python3
from __future__ import annotations

import argparse
from pathlib import Path
import sqlite3
import sys

from message_embeddings import MODEL_NAME, build_message_embeddings, semantic_message_search
from storage import get_database_path


def main():
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(errors="backslashreplace")
    parser = argparse.ArgumentParser(description="Local semantic message search")
    parser.add_argument("query", nargs="*", help="Query text; piped stdin is also accepted")
    parser.add_argument("--db", help="SQLite path; defaults to the same database as Archive Explorer")
    parser.add_argument("--model", default=MODEL_NAME, help="Sentence Transformers model name or local path")
    parser.add_argument("--rebuild", action="store_true", help="Incrementally index new or changed messages")
    parser.add_argument("--build-only", action="store_true", help="Build embeddings without searching")
    parser.add_argument("--batch-size", type=int, default=32)
    parser.add_argument("--limit", type=int, default=20)
    parser.add_argument("--role", choices=["user", "assistant", "system", "tool"])
    args = parser.parse_args()
    query = " ".join(args.query).strip()
    if not query and not args.build_only and not sys.stdin.isatty():
        query = sys.stdin.read().strip()
    if not args.build_only and not query:
        parser.error("provide a query or use --build-only")
    if args.limit < 1 or args.batch_size < 1:
        parser.error("--limit and --batch-size must be positive")
    database = Path(args.db) if args.db else get_database_path()
    try:
        # mode=rw prevents a typo from silently creating an empty database.
        conn = sqlite3.connect(database.resolve().as_uri() + "?mode=rw", uri=True)
        try:
            if args.rebuild or args.build_only:
                total, already, new, failures = build_message_embeddings(
                    conn, args.batch_size, model_name=args.model)
                print(f"Build: total={total}, cached={already}, new={new}, failures={len(failures)}")
                if failures:
                    for failure in failures[:10]:
                        print(failure)
                    return 1
            if args.build_only:
                return 0
            results = semantic_message_search(conn, query, args.limit, model_name=args.model, role=args.role)
            print(f"Database: {database}\nModel: {args.model}\n")
            for i, row in enumerate(results, 1):
                print(f"{i}. similarity={row['similarity']:.4f} role={row['role']} message_id={row['message_id']} conversation_id={row['conversation_id']} date={row['created_at']}")
                print(row["text"])
                print("-" * 60)
            return 0
        finally:
            conn.close()
    except (RuntimeError, ValueError, sqlite3.Error) as exc:
        print(f"Semantic search failed: {exc}", file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
