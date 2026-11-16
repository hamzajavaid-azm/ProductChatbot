-- Schema for the generic product RAG chatbot (single-tenant).
-- Run once against the pgvector-enabled Postgres server.
--
--   docker exec -i pgvector-db psql -U postgres -f - < Data/schema.sql
-- (or connect with psql and paste). Creating the database must be run while
-- connected to another DB such as 'postgres'.

-- CREATE DATABASE productbot;      -- run once, connected to the server (e.g. the 'postgres' db)
-- \c productbot

CREATE EXTENSION IF NOT EXISTS vector;

CREATE TABLE IF NOT EXISTS documents (
    id           BIGSERIAL PRIMARY KEY,
    title        TEXT NOT NULL,
    source_type  TEXT NOT NULL,              -- 'pdf' | 'excel' | 'csv' | 'text' | 'faq' | 'manual'
    file_name    TEXT,                       -- original upload name (null for manual entries)
    file_path    TEXT,                       -- path on disk under uploads/ (null for manual)
    content_hash TEXT,                       -- sha256 of source, for re-upload dedupe
    status       TEXT NOT NULL DEFAULT 'processed',  -- 'pending' | 'processed' | 'failed'
    metadata     JSONB NOT NULL DEFAULT '{}',
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE TABLE IF NOT EXISTS chunks (
    id           BIGSERIAL PRIMARY KEY,
    document_id  BIGINT NOT NULL REFERENCES documents(id) ON DELETE CASCADE,
    chunk_index  INT NOT NULL,              -- order within the document
    type         TEXT NOT NULL,             -- 'faq' | 'spec' | 'description' | 'policy' | 'doc'
    title        TEXT,                      -- e.g. the FAQ question / section heading
    content      TEXT NOT NULL,             -- the chunk text that gets embedded
    metadata     JSONB NOT NULL DEFAULT '{}',
    embedding    VECTOR(384) NOT NULL,
    created_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);

-- Approximate-nearest-neighbour index so vector search scales to large products.
CREATE INDEX IF NOT EXISTS chunks_embedding_hnsw
    ON chunks USING hnsw (embedding vector_cosine_ops);

CREATE INDEX IF NOT EXISTS chunks_document_id_idx ON chunks (document_id);
