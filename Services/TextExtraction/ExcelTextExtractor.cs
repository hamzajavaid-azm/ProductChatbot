using System.Text;
using ClosedXML.Excel;

namespace ProductChatbot.Services.TextExtraction
{
    public class ExcelTextExtractor : ITextExtractor
    {
        public IEnumerable<string> SupportedExtensions => new[] { ".xlsx" };

        public string ExtractText(Stream fileStream, string fileName)
        {
            var sb = new StringBuilder();
            using var workbook = new XLWorkbook(fileStream);
            foreach (var sheet in workbook.Worksheets)
            {
                sb.AppendLine($"# Sheet: {sheet.Name}");
                foreach (var row in sheet.RowsUsed())
                {
                    var cells = row.CellsUsed().Select(c => c.GetString());
                    sb.AppendLine(string.Join(" | ", cells));
                }
            }
            return sb.ToString();
        }
    }
}
