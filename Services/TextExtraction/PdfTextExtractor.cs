using System.Text;
using UglyToad.PdfPig;

namespace ProductChatbot.Services.TextExtraction
{
    public class PdfTextExtractor : ITextExtractor
    {
        public IEnumerable<string> SupportedExtensions => new[] { ".pdf" };

        public string ExtractText(Stream fileStream, string fileName)
        {
            var sb = new StringBuilder();
            using var pdf = PdfDocument.Open(fileStream);
            foreach (var page in pdf.GetPages())
                sb.AppendLine(page.Text);
            return sb.ToString();
        }
    }
}
