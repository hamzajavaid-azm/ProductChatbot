namespace ProductChatbot.Services.TextExtraction
{
    // Extracts plain text from an uploaded file. One implementation per file family; the
    // TextExtractorResolver maps file extensions to the right one, so adding a new format
    // is just adding a new ITextExtractor.
    public interface ITextExtractor
    {
        // Lowercase extensions handled, including the dot (e.g. ".pdf").
        IEnumerable<string> SupportedExtensions { get; }

        string ExtractText(Stream fileStream, string fileName);
    }
}
