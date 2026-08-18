using ChatAnalyzer.Core.Evidence;
using Microsoft.Data.Sqlite;

namespace ChatAnalyzer.Core.Storage;

public sealed class EvidenceEventStore
{
    private readonly string _connectionString;

    public EvidenceEventStore(string databasePath)
    {
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath
        }.ToString();
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS evidence_events (
                event_id TEXT PRIMARY KEY,
                conversation_id TEXT NOT NULL,
                exchange_id TEXT NOT NULL,
                occurred_at TEXT NULL,
                kind TEXT NOT NULL,
                text TEXT NOT NULL,
                confidence REAL NOT NULL,
                updated_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_evidence_events_occurred_at
                ON evidence_events(occurred_at);
            CREATE INDEX IF NOT EXISTS ix_evidence_events_exchange_id
                ON evidence_events(exchange_id);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(
        IEnumerable<EvidenceEvent> events,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        foreach (var item in events)
        {
            var command = connection.CreateCommand();
            command.Transaction = (SqliteTransaction)transaction;
            command.CommandText = """
                INSERT INTO evidence_events
                    (event_id, conversation_id, exchange_id, occurred_at, kind, text, confidence, updated_at)
                VALUES
                    ($id, $conversationId, $exchangeId, $occurredAt, $kind, $text, $confidence, $updatedAt)
                ON CONFLICT(event_id) DO UPDATE SET
                    conversation_id = excluded.conversation_id,
                    exchange_id = excluded.exchange_id,
                    occurred_at = excluded.occurred_at,
                    kind = excluded.kind,
                    text = excluded.text,
                    confidence = excluded.confidence,
                    updated_at = excluded.updated_at;
                """;
            command.Parameters.AddWithValue("$id", item.Id);
            command.Parameters.AddWithValue("$conversationId", item.ConversationId);
            command.Parameters.AddWithValue("$exchangeId", item.ExchangeId);
            command.Parameters.AddWithValue("$occurredAt", item.Timestamp?.ToString("O") ?? (object)DBNull.Value);
            command.Parameters.AddWithValue("$kind", item.Kind.ToString());
            command.Parameters.AddWithValue("$text", item.Text);
            command.Parameters.AddWithValue("$confidence", item.Confidence);
            command.Parameters.AddWithValue("$updatedAt", DateTimeOffset.UtcNow.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }
}
