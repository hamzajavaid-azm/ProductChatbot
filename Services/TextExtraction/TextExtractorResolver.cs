using System.Diagnostics.CodeAnalysis;

namespace ProductChatbot.Services.TextExtraction
{
    // Maps a file name's extension to the ITextExtractor that handles it. All registered
    // extractors are injected, so new formats light up automatically once registered in DI.
    public class TextExtractorResolver
    {
        private readonly Dictionary<string, ITextExtractor> _byExtension;

        public TextExtractorResolver(IEnumerable<ITextExtractor> extractors)
        {
            _byExtension = new Dictionary<string, ITextExtractor>(StringComparer.OrdinalIgnoreCase);
            foreach (var extractor in extractors)
                foreach (var ext in extractor.SupportedExtensions)
                    _byExtension[ext] = extractor;
        }

        public bool TryResolve(string fileName, [MaybeNullWhen(false)] out ITextExtractor extractor)
        {
            var ext = Path.GetExtension(fileName);
            return _byExtension.TryGetValue(ext, out extractor);
        }

        public string SupportedExtensionsList => string.Join(", ", _byExtension.Keys);
    }
}
