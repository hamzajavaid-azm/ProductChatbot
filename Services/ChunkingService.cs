using System.Text.RegularExpressions;

namespace ProductChatbot.Services
{
    // Splits extracted text into small, focused chunks before embedding. Each sentence/line
    // becomes its own chunk (tiny fragments like headings are merged forward) so a single fact
    // isn't diluted by unrelated text in the same vector — this is what makes document Q&A work.
    // A hard word cap keeps any one chunk under the embedding model's 256-token limit
    // (EmbeddingService uses maximumTokens: 256).
    public class ChunkingService
    {
        private readonly int _minWords;   // keep accumulating units until a chunk has at least this many words
        private readonly int _maxWords;   // hard cap; a longer single unit is windowed into pieces

        public ChunkingService(int minWords = 6, int maxWords = 200)
        {
            _minWords = minWords;
            _maxWords = maxWords;
        }

        public List<string> Chunk(string text)
        {
            var chunks = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
                return chunks;

            // Break into sentence/line units: split after . ! ? or on line breaks.
            var units = Regex.Split(text, @"(?<=[.!?])\s+|\r?\n+")
                .Select(u => u.Trim())
                .Where(u => u.Length > 0);

            var current = new List<string>();
            int currentWords = 0;

            void Flush()
            {
                if (current.Count > 0)
                {
                    chunks.Add(string.Join(' ', current));
                    current.Clear();
                    currentWords = 0;
                }
            }

            foreach (var unit in units)
            {
                var words = unit.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

                // A very long single unit (e.g. one huge paragraph) is windowed to stay under the cap.
                if (words.Length > _maxWords)
                {
                    Flush();
                    for (int i = 0; i < words.Length; i += _maxWords)
                        chunks.Add(string.Join(' ', words, i, Math.Min(_maxWords, words.Length - i)));
                    continue;
                }

                current.Add(unit);
                currentWords += words.Length;

                // Emit one chunk per unit, but roll tiny fragments (headings, labels) into the next.
                if (currentWords >= _minWords)
                    Flush();
            }

            Flush();
            return chunks;
        }
    }
}
