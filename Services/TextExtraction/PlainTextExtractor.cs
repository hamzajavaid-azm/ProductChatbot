namespace ProductChatbot.Services.TextExtraction
{
    // Handles already-textual formats. CSV is read as-is (delimiters preserved) — good enough
    // for a POC; a dedicated CSV extractor could be added later without touching callers.
    public class PlainTextExtractor : ITextExtractor
    {
        public IEnumerable<string> SupportedExtensions => new[] { ".txt", ".md", ".csv" };

        public string ExtractText(Stream fileStream, string fileName)
        {
            using var reader = new StreamReader(fileStream);
            return reader.ReadToEnd();
        }
    }
}
