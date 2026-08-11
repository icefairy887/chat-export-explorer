from __future__ import annotations

import os
import sqlite3
import tempfile
import unittest
from pathlib import Path
from unittest.mock import patch

import storage


class StorageTests(unittest.TestCase):
    def test_explicit_database_override_wins(self):
        with patch.dict(os.environ, {"CHAT_DB": "custom.db"}, clear=False):
            self.assertEqual(storage.get_database_path(), Path("custom.db"))

    def test_existing_database_is_migrated_once(self):
        with tempfile.TemporaryDirectory() as root:
            root_path = Path(root)
            project = root_path / "project"
            local = root_path / "local"
            project.mkdir()
            source = project / "chat_history.db"
            source.write_bytes(b"original")

            with (
                patch.dict(os.environ, {"LOCALAPPDATA": str(local)}, clear=False),
                patch.object(storage, "get_project_dir", return_value=project),
                patch.dict(os.environ, {}, clear=False),
            ):
                os.environ.pop("CHAT_DB", None)
                target = storage.get_database_path()
                self.assertEqual(target.read_bytes(), b"original")
                source.write_bytes(b"changed")
                storage.get_database_path()
                self.assertEqual(target.read_bytes(), b"original")


class HealthTests(unittest.TestCase):
    def test_health_is_ready_before_database_import(self):
        from app import app

        with tempfile.TemporaryDirectory() as root:
            app.config.update(TESTING=True, DATABASE=Path(root) / "missing.db")
            response = app.test_client().get("/health")

        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json["database_ready"], False)

    def test_health_reports_message_count(self):
        from app import app

        with tempfile.TemporaryDirectory() as root:
            database = Path(root) / "chat.db"
            connection = sqlite3.connect(database)
            connection.execute("CREATE TABLE messages (id INTEGER PRIMARY KEY)")
            connection.executemany("INSERT INTO messages DEFAULT VALUES", [(), ()])
            connection.commit()
            connection.close()

            app.config.update(TESTING=True, DATABASE=database)
            response = app.test_client().get("/health")

        self.assertEqual(response.status_code, 200)
        self.assertEqual(response.json["database_ready"], True)
        self.assertEqual(response.json["messages"], 2)


class SearchTests(unittest.TestCase):
    def test_search_falls_back_when_fts_table_is_missing(self):
        from app import app

        with tempfile.TemporaryDirectory() as root:
            database = Path(root) / "chat.db"
            connection = sqlite3.connect(database)
            connection.executescript(
                """
                CREATE TABLE conversations (
                    conversation_id TEXT PRIMARY KEY, title TEXT,
                    created_at TEXT, updated_at TEXT
                );
                CREATE TABLE messages (
                    message_id TEXT PRIMARY KEY, conversation_id TEXT,
                    parent_id TEXT, role TEXT, created_at TEXT,
                    text TEXT NOT NULL, metadata_json TEXT
                );
                INSERT INTO conversations VALUES ('c1', 'Test chat', NULL, NULL);
                INSERT INTO messages VALUES (
                    'm1', 'c1', NULL, 'user', NULL,
                    '<script>alert(1)</script> needle', NULL
                );
                """
            )
            connection.close()

            app.config.update(TESTING=True, DATABASE=database)
            response = app.test_client().get("/search?q=needle")

        page = response.get_data(as_text=True)
        self.assertEqual(response.status_code, 200)
        self.assertIn("<mark>needle</mark>", page)
        self.assertNotIn("<script>", page)


if __name__ == "__main__":
    unittest.main()
