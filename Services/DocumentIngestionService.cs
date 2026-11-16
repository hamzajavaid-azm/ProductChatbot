using Npgsql;
using NpgsqlTypes;
using Pgvector;

namespace ProductChatbot.Services
{
    // Turns source material into searchable rows: inserts one 'documents' row and N 'chunks' rows
    // (each embedded) inside a transaction. Plain Npgsql, matching the project's minimal-API style.
    public class DocumentIngestionService
    {
        private readonly DatabaseService _db;
        private readonly EmbeddingService _embedder;
        private readonly ChunkingService _chunker;

        public DocumentIngestionService(DatabaseService db, EmbeddingService embedder, ChunkingService chunker)
        {
            _db = db;
            _embedder = embedder;
            _chunker = chunker;
        }

        // Ingest a block of extracted text (from an uploaded file) as one document with N chunks.
        public async Task<(long DocumentId, int ChunkCount)> IngestTextAsync(
            string title, string sourceType, string text,
            string? fileName = null, string? filePath = null, string? contentHash = null,
            string chunkType = "doc")
        {
            var pieces = _chunker.Chunk(text);

            await using var conn = await _db.DataSource.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            long docId = await InsertDocumentAsync(conn, tx, title, sourceType, fileName, filePath, contentHash);

            int index = 0;
            foreach (var piece in pieces)
            {
                var embedding = _embedder.GetEmbedding(piece);
                await InsertChunkAsync(conn, tx, docId, index, chunkType, null, piece, embedding);
                index++;
            }

            await tx.CommitAsync();
            return (docId, index);
        }

        // Ingest a single manual knowledge item (FAQ, spec, policy, ...) as one document + one chunk.
        public async Task<(long DocumentId, int ChunkCount)> IngestContentAsync(
            string type, string? title, string content)
        {
            await using var conn = await _db.DataSource.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            var docTitle = string.IsNullOrWhiteSpace(title) ? $"{type} entry" : title!;
            long docId = await InsertDocumentAsync(conn, tx, docTitle, "manual", null, null, null);

            // Embed title + content together so a titled entry matches on either.
            var toEmbed = string.IsNullOrWhiteSpace(title) ? content : $"{title}\n{content}";
            var embedding = _embedder.GetEmbedding(toEmbed);
            await InsertChunkAsync(conn, tx, docId, 0, type, title, content, embedding);

            await tx.CommitAsync();
            return (docId, 1);
        }

        // Seed a set of FAQ Q&A pairs as a single 'faq' document. The question is embedded
        // (matching the original FAQ behaviour); the answer is stored as the chunk content.
        public async Task<(long DocumentId, int ChunkCount)> IngestFaqSetAsync(
            string title, IEnumerable<(string Question, string Answer)> faqs)
        {
            await using var conn = await _db.DataSource.OpenConnectionAsync();
            await using var tx = await conn.BeginTransactionAsync();

            long docId = await InsertDocumentAsync(conn, tx, title, "faq", null, null, null);

            int index = 0;
            foreach (var (question, answer) in faqs)
            {
                var embedding = _embedder.GetEmbedding(question);
                await InsertChunkAsync(conn, tx, docId, index, "faq", question, answer, embedding);
                index++;
            }

            await tx.CommitAsync();
            return (docId, index);
        }

        private static async Task<long> InsertDocumentAsync(
            NpgsqlConnection conn, NpgsqlTransaction tx,
            string title, string sourceType, string? fileName, string? filePath, string? contentHash)
        {
            await using var cmd = new NpgsqlCommand(
                @"INSERT INTO documents (title, source_type, file_name, file_path, content_hash)
                  VALUES ($1, $2, $3, $4, $5) RETURNING id;", conn, tx);
            cmd.Parameters.AddWithValue(title);
            cmd.Parameters.AddWithValue(sourceType);
            cmd.Parameters.Add(TextParam(fileName));
            cmd.Parameters.Add(TextParam(filePath));
            cmd.Parameters.Add(TextParam(contentHash));
            return (long)(await cmd.ExecuteScalarAsync())!;
        }

        private static async Task InsertChunkAsync(
            NpgsqlConnection conn, NpgsqlTransaction tx,
            long documentId, int chunkIndex, string type, string? title, string content, float[] embedding)
        {
            await using var cmd = new NpgsqlCommand(
                @"INSERT INTO chunks (document_id, chunk_index, type, title, content, embedding)
                  VALUES ($1, $2, $3, $4, $5, $6);", conn, tx);
            cmd.Parameters.AddWithValue(documentId);
            cmd.Parameters.AddWithValue(chunkIndex);
            cmd.Parameters.AddWithValue(type);
            cmd.Parameters.Add(TextParam(title));
            cmd.Parameters.AddWithValue(content);
            cmd.Parameters.AddWithValue(new Vector(embedding));
            await cmd.ExecuteNonQueryAsync();
        }

        // Nullable text parameter with an explicit type so DBNull doesn't break type inference.
        private static NpgsqlParameter TextParam(string? value) =>
            new() { Value = (object?)value ?? DBNull.Value, NpgsqlDbType = NpgsqlDbType.Text };
    }
}
