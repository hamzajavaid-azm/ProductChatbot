using ProductChatbot.Models;
using Npgsql;
using Pgvector;

namespace ProductChatbot.Services
{
    // Embeds the query and returns the top-K most similar chunks directly from Postgres using
    // pgvector's cosine-distance operator (<=>). Score is 1 - distance (cosine similarity).
    // This step is identical with or without an LLM — only the IAnswerer downstream differs.
    public class RetrievalService
    {
        private readonly DatabaseService _db;
        private readonly EmbeddingService _embedder;

        public RetrievalService(DatabaseService db, EmbeddingService embedder)
        {
            _db = db;
            _embedder = embedder;
        }

        public async Task<List<RetrievedChunk>> RetrieveAsync(string query, int k = 5)
        {
            var queryVector = _embedder.GetEmbedding(query);

            await using var conn = await _db.DataSource.OpenConnectionAsync();
            await using var cmd = new NpgsqlCommand(
                @"SELECT id, document_id, type, title, content, 1 - (embedding <=> $1) AS score
                  FROM chunks
                  ORDER BY embedding <=> $1
                  LIMIT $2;", conn);
            cmd.Parameters.AddWithValue(new Vector(queryVector));
            cmd.Parameters.AddWithValue(k);

            var results = new List<RetrievedChunk>();
            await using var reader = await cmd.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                results.Add(new RetrievedChunk(
                    reader.GetInt64(0),
                    reader.GetInt64(1),
                    reader.GetString(2),
                    reader.IsDBNull(3) ? null : reader.GetString(3),
                    reader.GetString(4),
                    reader.GetDouble(5)));
            }
            return results;
        }
    }
}
