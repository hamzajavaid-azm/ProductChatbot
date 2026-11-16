using ProductChatbot.Models;
using ProductChatbot.Services;
using ProductChatbot.Services.Answering;
using ProductChatbot.Services.TextExtraction;
using Npgsql;
using System.Security.Cryptography;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

// Embedding model (local ONNX all-MiniLM-L6-v2) and the pgvector-aware data source.
builder.Services.AddSingleton<EmbeddingService>(sp =>
    new EmbeddingService("MLModels/model.onnx", "MLModels/vocab.txt"));
builder.Services.AddSingleton(new DatabaseService(
    "Host=localhost;Port=5433;Database=productbot;Username=postgres;Password=mypassword"));

// Ingestion pipeline: text extractors (by file type) -> resolver -> chunker -> ingestion.
builder.Services.AddSingleton<ITextExtractor, PdfTextExtractor>();
builder.Services.AddSingleton<ITextExtractor, ExcelTextExtractor>();
builder.Services.AddSingleton<ITextExtractor, PlainTextExtractor>();
builder.Services.AddSingleton<TextExtractorResolver>();
builder.Services.AddSingleton<ChunkingService>();
builder.Services.AddSingleton<DocumentIngestionService>();

// Retrieval + the answer seam (no-LLM today; swap BestChunkAnswerer for an LlmAnswerer later).
builder.Services.AddSingleton<RetrievalService>();
builder.Services.AddSingleton<IAnswerer, BestChunkAnswerer>();

var app = builder.Build();

// Ensure the uploads folder exists (original files are stored on disk, not in the DB).
Directory.CreateDirectory(Path.Combine(app.Environment.ContentRootPath, "uploads"));

// Optional one-time seed: load Data/faq.json as a single 'faq' document if the store is empty.
{
    var ingest = app.Services.GetRequiredService<DocumentIngestionService>();
    var db = app.Services.GetRequiredService<DatabaseService>();

    await using var conn = await db.DataSource.OpenConnectionAsync();
    await using var countCmd = new NpgsqlCommand("SELECT COUNT(*) FROM documents;", conn);
    var count = (long)(await countCmd.ExecuteScalarAsync())!;

    if (count == 0 && File.Exists("Data/faq.json"))
    {
        var json = await File.ReadAllTextAsync("Data/faq.json");
        var faqs = JsonSerializer.Deserialize<List<FaqSeed>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<FaqSeed>();

        if (faqs.Count > 0)
            await ingest.IngestFaqSetAsync("Seed FAQ", faqs.Select(f => (f.Question, f.Answer)));
    }
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseHttpsRedirection();

app.UseDefaultFiles();
app.UseStaticFiles();


// Upload & ingest a file (pdf / xlsx / csv / txt / md): extract text -> chunk -> embed -> store.
app.MapPost("/documents/upload", async (
    IFormFile file,
    TextExtractorResolver resolver,
    DocumentIngestionService ingest,
    IWebHostEnvironment env) =>
{
    if (file is null || file.Length == 0)
        return Results.BadRequest(new { error = "No file uploaded." });

    if (!resolver.TryResolve(file.FileName, out var extractor))
        return Results.Problem(
            detail: $"Unsupported file type. Supported: {resolver.SupportedExtensionsList}",
            statusCode: StatusCodes.Status415UnsupportedMediaType);

    // Save the original file to disk under uploads/ with a collision-proof name.
    var uploadsDir = Path.Combine(env.ContentRootPath, "uploads");
    Directory.CreateDirectory(uploadsDir);
    var originalName = Path.GetFileName(file.FileName);
    var savedPath = Path.Combine(uploadsDir, $"{Guid.NewGuid():N}_{originalName}");

    await using (var fs = File.Create(savedPath))
        await file.CopyToAsync(fs);

    string contentHash;
    await using (var hashStream = File.OpenRead(savedPath))
        contentHash = Convert.ToHexString(await SHA256.HashDataAsync(hashStream));

    string extractedText;
    await using (var readStream = File.OpenRead(savedPath))
        extractedText = extractor.ExtractText(readStream, originalName);

    if (string.IsNullOrWhiteSpace(extractedText))
        return Results.BadRequest(new { error = "No text could be extracted from the file." });

    var sourceType = SourceTypeFromExtension(Path.GetExtension(originalName));
    var (documentId, chunkCount) = await ingest.IngestTextAsync(
        title: originalName,
        sourceType: sourceType,
        text: extractedText,
        fileName: originalName,
        filePath: savedPath,
        contentHash: contentHash,
        chunkType: "doc");

    return Results.Ok(new { documentId, chunkCount });
}).DisableAntiforgery();


// Add a manual knowledge item (FAQ, spec, policy, ...).
app.MapPost("/content", async (ContentRequest request, DocumentIngestionService ingest) =>
{
    if (string.IsNullOrWhiteSpace(request.Content))
        return Results.BadRequest(new { error = "Content is required." });

    var type = string.IsNullOrWhiteSpace(request.Type) ? "doc" : request.Type;
    var (documentId, chunkCount) = await ingest.IngestContentAsync(type, request.Title, request.Content);
    return Results.Ok(new { documentId, chunkCount });
});


// List ingested documents with their chunk counts.
app.MapGet("/documents", async (DatabaseService db) =>
{
    await using var conn = await db.DataSource.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand(
        @"SELECT d.id, d.title, d.source_type, d.status, d.created_at, COUNT(c.id) AS chunk_count
          FROM documents d
          LEFT JOIN chunks c ON c.document_id = d.id
          GROUP BY d.id
          ORDER BY d.created_at DESC;", conn);

    var list = new List<object>();
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
    {
        list.Add(new
        {
            id = reader.GetInt64(0),
            title = reader.GetString(1),
            sourceType = reader.GetString(2),
            status = reader.GetString(3),
            createdAt = reader.GetDateTime(4),
            chunkCount = reader.GetInt64(5)
        });
    }
    return Results.Ok(list);
});


// Delete a document and all its chunks (ON DELETE CASCADE).
app.MapDelete("/documents/{id:long}", async (long id, DatabaseService db) =>
{
    await using var conn = await db.DataSource.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand("DELETE FROM documents WHERE id = $1;", conn);
    cmd.Parameters.AddWithValue(id);
    var rows = await cmd.ExecuteNonQueryAsync();
    return rows > 0 ? Results.Ok(new { deleted = id }) : Results.NotFound();
});


// Ask a question: retrieve the top-K chunks, then let the IAnswerer produce the answer.
app.MapPost("/chat", async (ChatRequest request, RetrievalService retrieval, IAnswerer answerer) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
        return Results.BadRequest(new { error = "Message is required." });

    var topK = await retrieval.RetrieveAsync(request.Message, k: 5);
    var result = answerer.Answer(request.Message, topK);

    return Results.Ok(new
    {
        answer = result.Answer,
        score = result.Score,
        sources = result.Sources.Select(s => new { s.Id, s.DocumentId, s.Type, s.Title, s.Score })
    });
});


// Basic DB connectivity check.
app.MapGet("/db-check", async (DatabaseService db) =>
{
    await using var conn = await db.DataSource.OpenConnectionAsync();
    await using var cmd = new NpgsqlCommand("SELECT version();", conn);
    var result = await cmd.ExecuteScalarAsync();
    return Results.Ok(new { postgresVersion = result });
});

app.Run();


// Maps a file extension to the 'source_type' stored on the document row.
static string SourceTypeFromExtension(string ext) => ext.ToLowerInvariant() switch
{
    ".pdf" => "pdf",
    ".xlsx" => "excel",
    ".csv" => "csv",
    ".txt" or ".md" => "text",
    _ => "doc"
};
