from __future__ import annotations

import argparse
import json
import sqlite3
import sys
from datetime import datetime, timezone
from pathlib import Path
from typing import Any, Iterable


def load_json_documents(path: Path) -> list[Any]:
    """Load one or more JSON documents from a file.

    Supports:
      * a normal top-level JSON array
      * concatenated JSON arrays/objects
      * JSON Lines / NDJSON
    """
    text = path.read_text(encoding="utf-8-sig", errors="strict")
    decoder = json.JSONDecoder()
    documents: list[Any] = []
    index = 0
    length = len(text)

    while index < length:
        while index < length and text[index].isspace():
            index += 1
        if index >= length:
            break

        try:
            value, end = decoder.raw_decode(text, index)
        except json.JSONDecodeError as exc:
            context_start = max(0, exc.pos - 120)
            context_end = min(length, exc.pos + 120)
            context = text[context_start:context_end].replace("\n", "\\n")
            raise RuntimeError(
                f"Invalid JSON near line {exc.lineno}, column {exc.colno}, "
                f"character {exc.pos}. Nearby text: {context!r}"
            ) from exc

        documents.append(value)
        index = end

    return documents


def flatten_conversations(documents: Iterable[Any]) -> list[dict[str, Any]]:
    conversations: list[dict[str, Any]] = []

    for document in documents:
        if isinstance(document, list):
            conversations.extend(item for item in document if isinstance(item, dict))
        elif isinstance(document, dict):
            if isinstance(document.get("conversations"), list):
                conversations.extend(
                    item for item in document["conversations"] if isinstance(item, dict)
                )
            else:
                conversations.append(document)

    return conversations


def iso_time(value: Any) -> str | None:
    if not isinstance(value, (int, float)):
        return None
    try:
        return datetime.fromtimestamp(value, tz=timezone.utc).isoformat()
    except (OverflowError, OSError, ValueError):
        return None


def text_from_content(content: Any) -> str:
    if not isinstance(content, dict):
        return ""

    parts = content.get("parts")
    if isinstance(parts, list):
        rendered: list[str] = []
        for part in parts:
            if isinstance(part, str):
                rendered.append(part)
            elif isinstance(part, (dict, list)):
                rendered.append(json.dumps(part, ensure_ascii=False))
        return "\n".join(rendered).strip()

    text = content.get("text")
    return text.strip() if isinstance(text, str) else ""


def iter_messages(conversation: dict[str, Any]) -> Iterable[tuple[Any, ...]]:
    conversation_id = str(
        conversation.get("conversation_id")
        or conversation.get("id")
        or conversation.get("title")
        or "unknown"
    )
    mapping = conversation.get("mapping")
    if not isinstance(mapping, dict):
        return

    for node_id, node in mapping.items():
        if not isinstance(node, dict):
            continue
        message = node.get("message")
        if not isinstance(message, dict):
            continue

        author = message.get("author")
        role = author.get("role") if isinstance(author, dict) else None
        content = message.get("content")
        text = text_from_content(content)
        if not text:
            continue

        yield (
            str(message.get("id") or node_id),
            conversation_id,
            str(node.get("parent")) if node.get("parent") else None,
            role,
            iso_time(message.get("create_time")),
            text,
            json.dumps(message.get("metadata") or {}, ensure_ascii=False),
        )


def build_database(source: Path, database: Path) -> tuple[int, int, int]:
    print(f"Reading {source.name}...")
    documents = load_json_documents(source)
    conversations = flatten_conversations(documents)

    if not conversations:
        raise RuntimeError("No conversation objects were found in the export.")

    if database.exists():
        database.unlink()

    connection = sqlite3.connect(database)
    try:
        connection.execute("PRAGMA journal_mode=WAL")
        connection.execute("PRAGMA synchronous=NORMAL")
        connection.executescript(
            """
            CREATE TABLE conversations (
                conversation_id TEXT PRIMARY KEY,
                title TEXT,
                created_at TEXT,
                updated_at TEXT
            );

            CREATE TABLE messages (
                message_id TEXT PRIMARY KEY,
                conversation_id TEXT NOT NULL,
                parent_id TEXT,
                role TEXT,
                created_at TEXT,
                text TEXT NOT NULL,
                metadata_json TEXT,
                FOREIGN KEY (conversation_id) REFERENCES conversations(conversation_id)
            );

            CREATE INDEX idx_messages_conversation ON messages(conversation_id);
            CREATE INDEX idx_messages_role ON messages(role);
            CREATE INDEX idx_messages_created ON messages(created_at);

            CREATE VIRTUAL TABLE message_search USING fts5(
                message_id UNINDEXED,
                conversation_id UNINDEXED,
                title,
                role UNINDEXED,
                created_at UNINDEXED,
                text,
                tokenize='unicode61'
            );
            """
        )

        conversation_rows: list[tuple[Any, ...]] = []
        message_rows: list[tuple[Any, ...]] = []
        search_rows: list[tuple[Any, ...]] = []

        for position, conversation in enumerate(conversations, start=1):
            conversation_id = str(
                conversation.get("conversation_id")
                or conversation.get("id")
                or f"conversation-{position}"
            )
            title = str(conversation.get("title") or "Untitled")
            conversation_rows.append(
                (
                    conversation_id,
                    title,
                    iso_time(conversation.get("create_time")),
                    iso_time(conversation.get("update_time")),
                )
            )

            for row in iter_messages({**conversation, "conversation_id": conversation_id}):
                message_rows.append(row)
                search_rows.append((row[0], row[1], title, row[3], row[4], row[5]))

            if position % 100 == 0:
                print(f"Prepared {position:,}/{len(conversations):,} conversations...")

        connection.executemany(
            "INSERT OR REPLACE INTO conversations VALUES (?, ?, ?, ?)",
            conversation_rows,
        )
        connection.executemany(
            "INSERT OR REPLACE INTO messages VALUES (?, ?, ?, ?, ?, ?, ?)",
            message_rows,
        )
        connection.executemany(
            "INSERT INTO message_search VALUES (?, ?, ?, ?, ?, ?)",
            search_rows,
        )
        connection.commit()

        return len(documents), len(conversation_rows), len(message_rows)
    finally:
        connection.close()


def main() -> None:
    parser = argparse.ArgumentParser(description="Import a ChatGPT export into SQLite.")
    parser.add_argument("source", type=Path, help="Path to conversations.json")
    parser.add_argument(
        "--database",
        type=Path,
        default=Path("chat_history.db"),
        help="Output database path (default: chat_history.db)",
    )
    args = parser.parse_args()

    if not args.source.exists():
        print(f"Source file not found: {args.source}", file=sys.stderr)
        raise SystemExit(1)

    try:
        documents, conversations, messages = build_database(args.source, args.database)
    except Exception as exc:
        print(f"IMPORT FAILED: {exc}", file=sys.stderr)
        raise SystemExit(1) from exc

    print("\nIMPORT COMPLETE")
    print(f"JSON documents: {documents:,}")
    print(f"Conversations:  {conversations:,}")
    print(f"Messages:       {messages:,}")
    print(f"Database:       {args.database.resolve()}")


if __name__ == "__main__":
    main()
