using ProductChatbot.Models;

namespace ProductChatbot.Services.Answering
{
    // No-LLM answerer: returns the single best-matching chunk's text if it clears the similarity
    // threshold, otherwise a fallback message. Mirrors the original FAQ behaviour.
    public class BestChunkAnswerer : IAnswerer
    {
        private const double Threshold = 0.5;
        private const string Fallback = "Sorry, I don't have an answer for that.";

        public AnswerResult Answer(string question, IReadOnlyList<RetrievedChunk> topK)
        {
            // topK is ordered best-first (SQL orders by ascending cosine distance).
            var best = topK.Count > 0 ? topK[0] : null;

            if (best is null || best.Score < Threshold)
                return new AnswerResult(Fallback, best?.Score ?? -1.0, topK);

            return new AnswerResult(best.Content, best.Score, topK);
        }
    }
}
