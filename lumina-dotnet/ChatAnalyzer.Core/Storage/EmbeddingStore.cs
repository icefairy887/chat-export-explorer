using Microsoft.Data.Sqlite;

namespace ChatAnalyzer.Core.Storage;

public sealed class EmbeddingStore
{
    private readonly string _connectionString;

    public EmbeddingStore(string databasePath)
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
            CREATE TABLE IF NOT EXISTS exchange_embeddings (
                exchange_id TEXT PRIMARY KEY,
                dimensions INTEGER NOT NULL,
                embedding BLOB NOT NULL,
                created_at TEXT NOT NULL
            );
            """;

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<float[]?> GetAsync(
        string exchangeId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT dimensions, embedding
            FROM exchange_embeddings
            WHERE exchange_id = $exchangeId;
            """;

        command.Parameters.AddWithValue("$exchangeId", exchangeId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        if (!await reader.ReadAsync(cancellationToken))
            return null;

        return ReadEmbedding(reader);
    }

    public async Task<Dictionary<string, float[]>> GetAllAsync(
        CancellationToken cancellationToken = default)
    {
        var result = new Dictionary<string, float[]>();

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT exchange_id, dimensions, embedding
            FROM exchange_embeddings;
            """;

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            result[id] = ReadEmbedding(reader, 1, 2);
        }

        return result;
    }

    public async Task SaveAsync(
        string exchangeId,
        float[] embedding,
        CancellationToken cancellationToken = default)
    {
        var bytes = new byte[embedding.Length * sizeof(float)];
        Buffer.BlockCopy(embedding, 0, bytes, 0, bytes.Length);

        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();

        command.CommandText = """
            INSERT INTO exchange_embeddings
                (exchange_id, dimensions, embedding, created_at)
            VALUES
                ($exchangeId, $dimensions, $embedding, $createdAt)
            ON CONFLICT(exchange_id) DO UPDATE SET
                dimensions = excluded.dimensions,
                embedding = excluded.embedding,
                created_at = excluded.created_at;
            """;

        command.Parameters.AddWithValue("$exchangeId", exchangeId);
        command.Parameters.AddWithValue("$dimensions", embedding.Length);
        command.Parameters.AddWithValue("$embedding", bytes);
        command.Parameters.AddWithValue("$createdAt", DateTimeOffset.UtcNow.ToString("O"));

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<int> CountAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.CommandText = "SELECT COUNT(*) FROM exchange_embeddings;";

        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken));
    }

    private static float[] ReadEmbedding(
        SqliteDataReader reader,
        int dimensionsIndex = 0,
        int embeddingIndex = 1)
    {
        var dimensions = reader.GetInt32(dimensionsIndex);
        var bytes = (byte[])reader[embeddingIndex];

        var result = new float[dimensions];
        Buffer.BlockCopy(bytes, 0, result, 0, dimensions * sizeof(float));

        return result;
    }
}
