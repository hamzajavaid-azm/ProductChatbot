using ProductChatbot.Models;

namespace ProductChatbot.Services.Answering
{
    public record AnswerResult(string Answer, double Score, IReadOnlyList<RetrievedChunk> Sources);

    // The seam between retrieval and the final answer. Today a no-LLM implementation returns the
    // best matching chunk; a future LlmAnswerer can consume the same top-K chunks as prompt context
    // to generate an answer — swapping it is a one-line DI change, with no schema or retrieval change.
    public interface IAnswerer
    {
        AnswerResult Answer(string question, IReadOnlyList<RetrievedChunk> topK);
    }
}
