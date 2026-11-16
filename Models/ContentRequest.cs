namespace ProductChatbot.Models
{
    // Request body for POST /content — add a manual piece of knowledge (FAQ, spec, policy, ...).
    public record ContentRequest(string Type, string? Title, string Content);
}
