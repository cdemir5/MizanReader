using System.Text;
using System.Text.RegularExpressions;
using iText.Kernel.Pdf;
using iText.Kernel.Pdf.Canvas.Parser;



namespace TestApp
{
    public class LedgerEntry
    {
        public string? HesapKodu { get; init; }
        public string? Aciklama { get; init; }
        public string? Borc { get; init; }
        public string? Alacak { get; init; }
        public string? BakBorc { get; init; }
        public string? BakAlacak { get; init; }

        public override string ToString()
        {
            return $"HESAP KODU: {HesapKodu}\n" +
                   $"AÇIKLAMA: {Aciklama}\n" +
                   $"BORÇ: {Borc}\n" +
                   $"ALACAK: {Alacak}\n" +
                   $"BAK. BORÇ: {BakBorc}\n" +
                   $"BAK. ALACAK: {BakAlacak}";
        }
    }
    
    public class ParsedData
    {
        public string? DateRange { get; set; }
        public string? CustomerName { get; set; }
        public string? PageNumber { get; set; }
        public List<LedgerEntry> LedgerEntries { get; set; } = new List<LedgerEntry>();
    }
    
    public partial class PdfTemplateParser
    {
        // Regex pattern to match numbers like "3.168,81"
        private readonly Regex _numberRegex = MyRegex();

        public ParsedData Parse(string content)
        {
            var data = new ParsedData();

            // Split the text into lines.
            var lines = content.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);

            // 1. Parse Date Range (e.g., "01.01.2023 - 31.12.2023")
            var dateRangeMatch = Regex.Match(content, @"\d{2}\.\d{2}\.\d{4}\s*-\s*\d{2}\.\d{2}\.\d{4}");
            if (dateRangeMatch.Success)
                data.DateRange = dateRangeMatch.Value.Trim();

            // 2. Parse Customer Name.
            var dateLineIndex = Array.FindIndex(lines, l => data.DateRange != null && l.Contains(data.DateRange));
            if (dateLineIndex != -1 && dateLineIndex + 1 < lines.Length)
            {
                var custIndex = dateLineIndex + 1;
                while (custIndex < lines.Length && string.IsNullOrWhiteSpace(lines[custIndex]))
                    custIndex++;
                if (custIndex < lines.Length)
                    data.CustomerName = lines[custIndex].Trim();
            }

            // 3. Parse Page Number.
            var pageLine = lines.FirstOrDefault(l => l.Contains("Sayfa No"));
            if (!string.IsNullOrEmpty(pageLine))
            {
                var pageMatch = Regex.Match(pageLine, @"\d+\s*/\s*\d+");
                if (pageMatch.Success)
                    data.PageNumber = pageMatch.Value.Trim();
            }

            // 4. Locate the header line for ledger entries.
            var headerIndex = Array.FindIndex(lines, l => l.Contains("HESAP KODU") &&
                                                          l.Contains("AÇIKLAMA") &&
                                                          l.Contains("BORÇ"));
            if (headerIndex == -1)
            {
                return data;
            }

            for (var i = headerIndex + 1; i < lines.Length; i++)
            {
                var line = lines[i].Trim();
                if (string.IsNullOrEmpty(line))
                    continue;
                if (line.Contains("Tarihleri Arası Mizan") ||
                    line.Contains("Sayfa No") ||
                    line.StartsWith("GENEL TOPLAM"))
                    continue;

                var tokens = line.Split([' '], StringSplitOptions.RemoveEmptyEntries);
                if (tokens.Length < 5)
                    continue; // Not enough tokens to be valid

                // Find the index of the first token that matches a numeric value.
                var numericStartIndex = -1;
                for (var j = 0; j < tokens.Length; j++)
                {
                    if (_numberRegex.IsMatch(tokens[j]))
                    {
                        numericStartIndex = j;
                        break;
                    }
                }
                if (numericStartIndex == -1)
                    continue; // No financial columns found

                // The tokens before the numeric start are for account code and description.
                var accountTokens = tokens.Take(numericStartIndex).ToList();
                string hesapKodu, aciklama;

                switch (accountTokens.Count)
                {
                    case 1:
                        hesapKodu = accountTokens[0];
                        aciklama = "";
                        break;
                    // New logic based on whether the second token is numeric.
                    case >= 2 when IsAllDigits(accountTokens[1]):
                    {
                        switch (accountTokens.Count)
                        {
                            // Second token is numeric.
                            // Check if a third token exists and is a 4-digit year.
                            case >= 3 when
                                accountTokens[2].Length == 4 &&
                                int.TryParse(accountTokens[2], out var year) &&
                                year is >= 1900 and <= 2100:
                                // In this case, only the first two tokens form the account code.
                                hesapKodu = accountTokens[0] + " " + accountTokens[1];
                                aciklama = string.Join(" ", accountTokens.Skip(2));
                                break;
                            case >= 3 when IsAllDigits(accountTokens[2]):
                                // Both second and third tokens are numeric (and not a 4-digit year),
                                // so include them in the account code.
                                hesapKodu = string.Join(" ", accountTokens.Take(3));
                                aciklama = accountTokens.Count > 3 ? string.Join(" ", accountTokens.Skip(3)) : "";
                                break;
                            default:
                                // Second token is numeric but third is not numeric.
                                // Use the first two tokens as the account code.
                                hesapKodu = string.Join(" ", accountTokens.Take(2));
                                aciklama = accountTokens.Count > 2 ? string.Join(" ", accountTokens.Skip(2)) : "";
                                break;
                        }

                        break;
                    }
                    case >= 2:
                        // Second token is not numeric.
                        // In this case, only the first token is the account code.
                        hesapKodu = accountTokens[0];
                        aciklama = string.Join(" ", accountTokens.Skip(1));
                        break;
                    default:
                        // Fallback
                        hesapKodu = "";
                        aciklama = "";
                        break;
                }

                // The tokens from numericStartIndex onward are the financial columns.
                var financialTokens = tokens.Skip(numericStartIndex).ToList();
                string? borc;
                string? alacak;
                string? bakBorc;
                string? bakAlacak;
                if (financialTokens.Count == 4)
                {
                    borc = financialTokens[0];
                    alacak = financialTokens[1];
                    bakBorc = financialTokens[2];
                    bakAlacak = financialTokens[3];
                }
                else if (financialTokens.Count == 3)
                {
                    borc = financialTokens[0];
                    alacak = financialTokens[1];
                    bakBorc = financialTokens[2];
                    bakAlacak = null;
                }
                else
                {
                    borc = financialTokens.ElementAtOrDefault(0);
                    alacak = financialTokens.ElementAtOrDefault(1);
                    bakBorc = financialTokens.ElementAtOrDefault(2);
                    bakAlacak = financialTokens.Count > 3 ? string.Join(" ", financialTokens.Skip(3)) : null;
                }

                var entry = new LedgerEntry
                {
                    HesapKodu = hesapKodu,
                    Aciklama = aciklama,
                    Borc = borc,
                    Alacak = alacak,
                    BakBorc = bakBorc,
                    BakAlacak = bakAlacak
                };

                data.LedgerEntries.Add(entry);
            }

            return data;
        }

        // Helper method to check if a string is composed entirely of digits.
        private bool IsAllDigits(string s)
        {
            return s.All(char.IsDigit);
        }

        [GeneratedRegex(@"^\d{1,3}(?:\.\d{3})*,\d+$")]
        private static partial Regex MyRegex();
    }
    
    public class PdfReaderHelper
    {
        public static string ExtractTextFromPdf(string path)
        {
            var text = new StringBuilder();
            using var pdfReader = new PdfReader(path);
            using var pdfDoc = new PdfDocument(pdfReader);
            var numberOfPages = pdfDoc.GetNumberOfPages();
            for (var i = 1; i <= numberOfPages; i++)
            {
                var page = pdfDoc.GetPage(i);
                var pageText = PdfTextExtractor.GetTextFromPage(page);
                text.AppendLine(pageText);
            }

            return text.ToString();
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            // Specify the path to your PDF file.
            //const string pdfFilePath = "CANDAN 2023 YILI MİZANI.pdf"; // Adjust this path if needed.
            const string pdfFilePath = "MI\u0307ZAN 2024-2.pdf"; // Adjust this path if needed.
            // Read the PDF content.
            var pdfContent = PdfReaderHelper.ExtractTextFromPdf(pdfFilePath);

            // Create an instance of the parser and parse the PDF content.
            var parser = new PdfTemplateParser();
            var result = parser.Parse(pdfContent);

            // Output the parsed information.
            Console.WriteLine("DateRange: " + result.DateRange);
            Console.WriteLine("CustomerName: " + result.CustomerName);
            Console.WriteLine("PageCount: " + result.PageNumber?.Split('/')[1]);
            Console.WriteLine("\nParsed Ledger Entries:");
            foreach (var entry in result.LedgerEntries)
            {
                Console.WriteLine(entry);
                Console.WriteLine("---------------");
            }
        }
    }
}





