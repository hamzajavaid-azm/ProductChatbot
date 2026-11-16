namespace ProductChatbot.Models
{
    // A chunk returned from a vector search, with its similarity score (1 - cosine distance).
    // Retrieval returns the top-K of these; the IAnswerer decides how they become an answer.
    public record RetrievedChunk(
        long Id,
        long DocumentId,
        string Type,
        string? Title,
        string Content,
        double Score);
}
