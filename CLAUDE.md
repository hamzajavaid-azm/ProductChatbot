# CLAUDE.md

Guidance for Claude Code (and other AI agents) working in this repository.

## What this project is

**ProductChatbot** is a learning proof-of-concept that grew from an FAQ demo into a **generic product
chatbot** built on ASP.NET Core **minimal APIs**. It is a **RAG retrieval** system: you load a
product's knowledge (hand-entered items *and* uploaded documents), it embeds everything into
384-dim vectors with a local ONNX model (`all-MiniLM-L6-v2`), and answers questions by vector
similarity search in PostgreSQL + **pgvector**.

It works **without an LLM today** (returns the best-matching chunk of text) and is built so an LLM
can be added later for generative answers **without any schema change or re-ingestion** — see the
answer seam below.

Scope notes: single-tenant (one product per deployment), learning POC. Prefer small, readable
changes over heavy abstraction (no repositories / EF Core / layered architecture — plain Npgsql).

## Project layout

```
ProductChatbot/
├── Program.cs               Minimal API: DI, startup seed, all endpoints
├── Models/                 C# records/classes
│   ├── ChatRequest.cs       record ChatRequest(string Message)
│   ├── ContentRequest.cs    record ContentRequest(string Type, string? Title, string Content)
│   ├── RetrievedChunk.cs    a chunk + similarity score from a vector search
│   └── FaqSeed.cs           shape of Data/faq.json (startup seed only)
├── Services/
│   ├── EmbeddingService.cs       ONNX model + tokenizer, GetEmbedding(text)->float[384]
│   ├── DatabaseService.cs        NpgsqlDataSource with .UseVector()
│   ├── ChunkingService.cs        splits text into ~200-word overlapping chunks
│   ├── DocumentIngestionService.cs  extract->chunk->embed->insert (documents + chunks)
│   ├── RetrievalService.cs       embed query -> top-K via pgvector `<=>`
│   ├── TextExtraction/           ITextExtractor + Pdf/Excel/PlainText + resolver
│   └── Answering/                IAnswerer + BestChunkAnswerer (the LLM seam)
├── Data/
│   ├── faq.json             optional seed content
│   └── schema.sql           DB schema (documents, chunks, HNSW index)
├── MLModels/                model.onnx (~86 MB) + vocab.txt (committed; app runs on clone)
├── wwwroot/index.html       no-framework chat UI with file upload, posts to /chat
├── uploads/                 original uploaded files (gitignored, created at runtime)
└── .claude/                 AI-dev config
```

Namespaces follow folders: `ProductChatbot.Models`, `ProductChatbot.Services`,
`ProductChatbot.Services.TextExtraction`, `ProductChatbot.Services.Answering`.

## Data model (PostgreSQL + pgvector)

Database **`productbot`**. Two tables — everything searchable lives in **one** vector table
(`chunks`); `documents` is the parent that tracks where chunks came from. Full DDL in
`Data/schema.sql`.

- `documents(id, title, source_type, file_name, file_path, content_hash, status, metadata, created_at)`
  — one row per source (uploaded file, manual entry, or the FAQ seed set).
- `chunks(id, document_id FK ON DELETE CASCADE, chunk_index, type, title, content, metadata jsonb,
  embedding vector(384), created_at)` — the embedded retrieval unit. `type` ∈
  faq|spec|description|policy|doc. HNSW index on `embedding` for scale.

FAQs are just `type='faq'` chunks (title=question, content=answer). **Multi-tenant later** = add a
`products` table + `product_id` column and a `WHERE product_id = $x`; additive, not a rewrite.

## Endpoints

| Method | Route                | Purpose |
|--------|----------------------|---------|
| POST   | `/documents/upload`  | Upload & ingest a file (pdf/xlsx/csv/txt/md) → `{documentId, chunkCount}` |
| POST   | `/content`           | Add a manual item `{type, title, content}` → embeds & stores |
| GET    | `/documents`         | List ingested documents with chunk counts |
| DELETE | `/documents/{id}`    | Delete a document and its chunks (cascade) |
| POST   | `/chat`              | `{message}` → retrieve top-K → `IAnswerer` → `{answer, score, sources}` |
| GET    | `/db-check`          | DB connectivity check (`SELECT version()`) |

## The LLM seam (important)

Retrieval always does: **embed question → vector-search `chunks` → top-K**. `IAnswerer`
(`Services/Answering/`) turns those chunks into an answer:
- **`BestChunkAnswerer`** (current, no LLM): returns the best chunk if score ≥ `0.5`, else the
  `"Sorry, I don't have an answer for that."` fallback.
- **Future `LlmAnswerer`**: feed the same top-K into a prompt as context, call an LLM. Swapping is a
  one-line DI change in `Program.cs`. Retrieval already returns top-K so the context is ready.

## Running it

Prerequisites: .NET 10 SDK; Docker Postgres+pgvector (`pgvector-db`) reachable at
`Host=localhost;Port=5433;Username=postgres;Password=mypassword`.

One-time DB setup (creates the `productbot` database + schema):
```bash
docker exec -i pgvector-db psql -U postgres -c "CREATE DATABASE productbot;"
docker exec -i pgvector-db psql -U postgres -d productbot < Data/schema.sql
```

Run:
```bash
dotnet run --project . --launch-profile http   # http://localhost:5246
```
First run seeds `Data/faq.json` as one `faq` document if `documents` is empty.

## Conventions & gotchas

- **Minimal-API style** — endpoints live in `Program.cs`; plain `NpgsqlCommand`, no EF Core. Match
  the existing endpoints. File uploads use `IFormFile` + `.DisableAntiforgery()`.
- **Chunking is mandatory** — the embedder caps input at **256 tokens** (`maximumTokens: 256`), so
  long files must be split (`ChunkingService`, ~200 words) before embedding.
- **pgvector parameters** use `Pgvector.Vector`; `DatabaseService` calls `.UseVector()` so Npgsql
  maps it to the `vector` column. Nullable text params use an explicit `NpgsqlDbType.Text` so
  `DBNull` doesn't break type inference.
- **Seeding is idempotent** via an empty-`documents` check. To re-seed, clear the tables
  (`TRUNCATE chunks, documents RESTART IDENTITY;`) and restart.
- **Original files** are saved under `uploads/` (gitignored); only extracted text + embeddings go
  in the DB. The file path is recorded on the `documents` row.
- **Stop a running instance before building** — a live IIS Express / `dotnet` process locks
  `bin/.../ProductChatbot.dll` and the build fails with a file-copy error (not a compile error).
- The DB connection string (incl. a throwaway local password) is hardcoded in `Program.cs`. Fine for
  this POC; move to configuration/user-secrets if it grows up.
