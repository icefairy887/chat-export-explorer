import io
import json
from pathlib import Path
import sqlite3
import tempfile
import unittest
from unittest.mock import patch

import numpy as np

import message_embeddings as embeddings
from evaluate_embeddings import load_cases, score_rankings


class FakeEncoder:
    def __init__(self):
        self.queries = []
        self.documents = []

    def encode_document(self, texts, **kwargs):
        self.documents.extend(texts)
        return np.array([[1, 0] if 'job' in text else [0, 1] for text in texts], dtype=np.float32)

    def encode_query(self, texts, **kwargs):
        self.queries.extend(texts)
        return np.array([[1, 0] for _ in texts], dtype=np.float32)


def fixture(conn):
    conn.executescript('''
        CREATE TABLE conversations (conversation_id TEXT PRIMARY KEY, title TEXT);
        CREATE TABLE messages (message_id TEXT PRIMARY KEY, conversation_id TEXT,
            created_at TEXT, role TEXT, text TEXT);
        INSERT INTO conversations VALUES ('c1', 'Fixture');
        INSERT INTO messages VALUES ('m1', 'c1', '2026-01-01', 'user', 'job invitation');
        INSERT INTO messages VALUES ('m2', 'c1', '2026-01-02', 'assistant', 'job advice');
        INSERT INTO messages VALUES ('m3', 'c1', '2026-01-03', 'user', 'a cat');
    ''')


class EmbeddingTests(unittest.TestCase):
    def setUp(self):
        self.conn = sqlite3.connect(':memory:')
        fixture(self.conn)
        self.encoder = FakeEncoder()
        self.mock = patch.object(embeddings, 'get_encoder', return_value=self.encoder)
        self.mock.start()
        self.addCleanup(self.mock.stop)
        self.addCleanup(self.conn.close)

    def test_legacy_migration_preserves_blobs_and_adds_second_model(self):
        blob = np.array([1, 0], dtype=np.float32).tobytes()
        self.conn.execute('''CREATE TABLE message_embeddings (message_id TEXT PRIMARY KEY,
            embedding BLOB, dim INTEGER, norm REAL, model_name TEXT, created_at TEXT)''')
        self.conn.execute('INSERT INTO message_embeddings VALUES (?, ?, 2, 1, ?, NULL)',
                          ('m1', blob, embeddings.MODEL_NAME))
        self.conn.commit()
        embeddings.build_message_embeddings(self.conn, model_name='candidate')
        self.assertEqual(self.conn.execute('SELECT embedding FROM message_embeddings WHERE model_name=?',
                                          (embeddings.MODEL_NAME,)).fetchone()[0], blob)
        self.assertEqual(self.conn.execute('SELECT count(*) FROM message_embeddings').fetchone()[0], 4)
        embeddings.ensure_message_embedding_schema(self.conn)
        self.assertEqual(self.conn.execute('SELECT count(*) FROM message_embeddings').fetchone()[0], 4)

    def test_incremental_build_and_edited_text_invalidation(self):
        self.assertEqual(embeddings.build_message_embeddings(self.conn)[2], 3)
        self.assertEqual(embeddings.build_message_embeddings(self.conn)[1:3], (3, 0))
        self.conn.execute("UPDATE messages SET text='a changed cat' WHERE message_id='m1'")
        results = embeddings.semantic_message_search(self.conn, 'work')
        self.assertNotIn('m1', [r['message_id'] for r in results])
        self.assertEqual(embeddings.build_message_embeddings(self.conn)[1:3], (2, 1))
        results = embeddings.semantic_message_search(self.conn, 'work')
        self.assertEqual(results[0]['message_id'], 'm2')

    def test_role_filter_applies_before_limit_and_query_encoding_is_distinct(self):
        embeddings.build_message_embeddings(self.conn)
        results = embeddings.semantic_message_search(self.conn, 'work', limit=1, role='user')
        self.assertEqual(results[0]['message_id'], 'm1')
        self.assertEqual(self.encoder.queries, ['work'])
        self.assertEqual(len(self.encoder.documents), 3)

    def test_bad_vectors_are_excluded_instead_of_ranked_as_zero(self):
        embeddings.build_message_embeddings(self.conn)
        self.conn.execute("UPDATE message_embeddings SET embedding=? WHERE message_id='m1'",
                          (np.array([float('nan'), 0], dtype=np.float32).tobytes(),))
        self.conn.execute("UPDATE message_embeddings SET dim=100 WHERE message_id='m2'")
        result = embeddings.semantic_message_search(self.conn, 'work')
        self.assertEqual([r['message_id'] for r in result], ['m3'])

    def test_failure_rolls_back_entire_batch(self):
        embeddings.ensure_message_embedding_schema(self.conn)
        self.conn.execute("""CREATE TRIGGER reject_second BEFORE INSERT ON message_embeddings
            WHEN NEW.message_id='m2' BEGIN SELECT RAISE(ABORT, 'fixture failure'); END""")
        total, already, new, failures = embeddings.build_message_embeddings(self.conn)
        self.assertEqual((total, already, new, len(failures)), (3, 0, 0, 3))
        self.assertEqual(self.conn.execute('SELECT count(*) FROM message_embeddings').fetchone()[0], 0)

    def test_no_index_does_not_load_model_or_create_table(self):
        with self.assertRaisesRegex(RuntimeError, 'No semantic index'):
            embeddings.semantic_message_search(self.conn, 'job')
        self.assertFalse(self.conn.execute('PRAGMA table_info(message_embeddings)').fetchall())
        self.assertFalse(self.encoder.queries)

    def test_invalid_query_and_limit(self):
        for query, limit in [('', 2), ('  ', 2), ('job', 0), ('job', -1)]:
            with self.assertRaises(ValueError):
                embeddings.semantic_message_search(self.conn, query, limit)


class SearchRouteTests(unittest.TestCase):
    def setUp(self):
        from app import app
        self.app = app
        self.root = tempfile.TemporaryDirectory()
        self.addCleanup(self.root.cleanup)
        self.path = Path(self.root.name) / 'fixture.db'
        conn = sqlite3.connect(self.path)
        fixture(conn)
        self.encoder = FakeEncoder()
        with patch.object(embeddings, 'get_encoder', return_value=self.encoder):
            embeddings.build_message_embeddings(conn)
        conn.commit()
        conn.close()
        self.old_config = dict(app.config)
        self.addCleanup(lambda: app.config.update(self.old_config))
        app.config.update(TESTING=True, DATABASE=self.path, SEMANTIC_MODEL=embeddings.MODEL_NAME)

    def test_meaning_search_returns_source_links_and_escapes_text(self):
        conn = sqlite3.connect(self.path)
        conn.execute("UPDATE messages SET text='<script>job</script>' WHERE message_id='m1'")
        with patch.object(embeddings, 'get_encoder', return_value=self.encoder):
            embeddings.build_message_embeddings(conn)
            conn.commit()
            response = self.app.test_client().get('/search?q=work&mode=semantic&role=user')
        conn.close()
        page = response.get_data(as_text=True)
        self.assertEqual(response.status_code, 200)
        self.assertIn('/conversation/c1#m1', page)
        self.assertIn('&lt;script&gt;job&lt;/script&gt;', page)
        self.assertNotIn('<script>job', page)
        self.assertNotIn('job advice', page)

    def test_missing_model_index_shows_actionable_error(self):
        self.app.config['SEMANTIC_MODEL'] = 'not-indexed'
        response = self.app.test_client().get('/search?q=work&mode=semantic')
        self.assertEqual(response.status_code, 200)
        self.assertIn('No indexed messages', response.get_data(as_text=True))

    def test_invalid_fts_expression_does_not_crash(self):
        conn = sqlite3.connect(self.path)
        conn.execute('''CREATE VIRTUAL TABLE message_search USING fts5(
            message_id, conversation_id, title, role, created_at, text)''')
        conn.close()
        response = self.app.test_client().get('/search', query_string={'q':'"unclosed'})
        self.assertEqual(response.status_code, 200)
        self.assertIn('expression is invalid', response.get_data(as_text=True))


class EvaluationTests(unittest.TestCase):
    def test_metrics_have_known_values(self):
        result = score_rankings(np.array([[.2, .9, .8]]),
                                [{'id':'a'}, {'id':'b'}, {'id':'c'}],
                                [{'id':'q', 'relevant':['a', 'c']}], 2)
        self.assertEqual(result['metrics']['hit_at_1'], 0)
        self.assertEqual(result['metrics']['recall_at_k'], .5)
        self.assertEqual(result['metrics']['mrr_at_k'], .5)
        self.assertAlmostEqual(result['metrics']['ndcg_at_k'], .3868528072)

    def test_fixture_is_valid(self):
        documents, queries = load_cases(Path(__file__).parent / 'fixtures/retrieval.jsonl')
        self.assertEqual(len(documents), 30)
        self.assertEqual(len(queries), 14)

    def test_cli_database_typo_does_not_create_a_file(self):
        import semantic_search
        with tempfile.TemporaryDirectory() as root:
            path = Path(root) / 'missing.db'
            with patch('sys.argv', ['semantic_search.py', '--db', str(path), 'job']):
                with patch('sys.stderr', new=io.StringIO()):
                    self.assertEqual(semantic_search.main(), 1)
            self.assertFalse(path.exists())


if __name__ == '__main__':
    unittest.main()
