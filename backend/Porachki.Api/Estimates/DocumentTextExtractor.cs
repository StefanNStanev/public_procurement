using System.IO.Compression;
using System.Security.Cryptography.Pkcs;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using SharpCompress.Archives;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;

namespace Porachki.Api.Estimates;

/// <summary>Резултат от обработката на един файл (или файл от архив).</summary>
public record ExtractedFile(string Name, string Status, string? Text = null, byte[]? NativePdf = null, int Pages = 0);

/// <summary>
/// Извлича текст от типичните формати в документацията на обществени поръчки:
/// PDF, DOCX, XLSX, ODT/ODS, RTF, DOC/XLS (приблизително), TXT/CSV/HTML/XML,
/// архиви ZIP/RAR/7z и подписани с КЕП .p7m файлове.
/// </summary>
public partial class DocumentTextExtractor
{
    private const int MaxDepth = 4;
    private static readonly Encoding Cp1251;

    static DocumentTextExtractor()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        Cp1251 = Encoding.GetEncoding(1251);
    }

    public IEnumerable<ExtractedFile> Extract(string name, byte[] data, int depth = 0)
    {
        if (depth > MaxDepth) return [new ExtractedFile(name, "пропуснат: твърде дълбоко вложен архив")];
        try
        {
            return ExtractCore(name, data, depth);
        }
        catch (Exception ex)
        {
            return [new ExtractedFile(name, $"грешка при четене: {ex.GetType().Name}")];
        }
    }

    private IEnumerable<ExtractedFile> ExtractCore(string name, byte[] data, int depth)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();

        if (ext is ".p7s") return [new ExtractedFile(name, "пропуснат: електронен подпис")];
        if (ext is ".p7m" or ".p7c" || (ext == ".p7" && data.Length > 0)) return FromSigned(name, data, depth);

        if (StartsWith(data, "%PDF")) return [FromPdf(name, data)];

        if (StartsWith(data, "PK"))
        {
            if (ext is ".docx" or ".docm" or ".dotx" || HasZipEntry(data, "word/document.xml")) return [Text(name, FromDocx(data))];
            if (ext is ".xlsx" or ".xlsm" || HasZipEntry(data, "xl/workbook.xml")) return [Text(name, FromXlsx(data))];
            if (ext is ".odt" or ".ods" or ".odp" || HasZipEntry(data, "content.xml")) return [Text(name, FromOdf(data))];
            if (ext is ".pptx") return [new ExtractedFile(name, "пропуснат: презентация")];
            return FromArchive(name, data, depth);
        }
        if (StartsWith(data, "Rar!") || (data.Length > 6 && data[0] == '7' && data[1] == 'z' && data[2] == 0xBC && data[3] == 0xAF))
            return FromArchive(name, data, depth);

        if (data.Length > 8 && data[0] == 0xD0 && data[1] == 0xCF && data[2] == 0x11 && data[3] == 0xE0)
            return [Text(name, FromOleHeuristic(data), approximate: true)];

        if (StartsWith(data, "{\\rtf")) return [Text(name, FromRtf(data))];

        if (ext is ".txt" or ".csv" or ".xml" or ".html" or ".htm" or ".json" or ".md")
        {
            var s = Decode(data);
            if (ext is ".html" or ".htm" or ".xml") s = Tags().Replace(s, " ");
            return [Text(name, s)];
        }

        if (ext is ".jpg" or ".jpeg" or ".png" or ".tif" or ".tiff" or ".bmp" or ".gif")
            return [new ExtractedFile(name, "пропуснат: изображение")];
        if (ext is ".dwg" or ".dxf") return [new ExtractedFile(name, "пропуснат: чертеж (CAD)")];

        return [new ExtractedFile(name, $"пропуснат: неподдържан формат {ext}")];
    }

    // ---------- PDF ----------

    private static ExtractedFile FromPdf(string name, byte[] data)
    {
        using var pdf = PdfDocument.Open(data);
        var sb = new StringBuilder();
        var pages = 0;
        foreach (var page in pdf.GetPages())
        {
            pages++;
            string text;
            try { text = ContentOrderTextExtractor.GetText(page); }
            catch { text = page.Text; }
            sb.Append("[стр. ").Append(page.Number).Append("]\n").Append(text).Append('\n');
        }
        var result = Normalize(sb.ToString());
        var meaningful = Letters().Matches(result).Count;
        // Почти без текст → сканиран документ
        if (pages > 0 && meaningful < 40 * pages)
            return new ExtractedFile(name, "сканиран PDF", Text: meaningful > 0 ? result : null, NativePdf: data, Pages: pages);
        return new ExtractedFile(name, "ok", result, Pages: pages);
    }

    // ---------- Office Open XML ----------

    private static string FromDocx(byte[] data)
    {
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        var sb = new StringBuilder();
        foreach (var entryName in new[] { "word/document.xml", "word/footnotes.xml" })
        {
            var entry = zip.GetEntry(entryName);
            if (entry is null) continue;
            using var s = entry.Open();
            using var r = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
            var inText = false;
            while (r.Read())
            {
                switch (r.NodeType)
                {
                    case XmlNodeType.Element when r.LocalName == "t": inText = !r.IsEmptyElement; break;
                    case XmlNodeType.Element when r.LocalName == "tab": sb.Append('\t'); break;
                    case XmlNodeType.Element when r.LocalName is "br" or "cr": sb.Append('\n'); break;
                    case XmlNodeType.Text or XmlNodeType.SignificantWhitespace when inText: sb.Append(r.Value); break;
                    case XmlNodeType.EndElement when r.LocalName == "t": inText = false; break;
                    case XmlNodeType.EndElement when r.LocalName == "p": sb.Append('\n'); break;
                    case XmlNodeType.EndElement when r.LocalName == "tc": sb.Append(" | "); break;
                }
            }
        }
        return sb.ToString();
    }

    private static string FromXlsx(byte[] data)
    {
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);

        var shared = new List<string>();
        if (zip.GetEntry("xl/sharedStrings.xml") is { } ss)
        {
            using var s = ss.Open();
            using var r = XmlReader.Create(s);
            StringBuilder? cur = null;
            var inText = false;
            while (r.Read())
            {
                switch (r.NodeType)
                {
                    case XmlNodeType.Element when r.LocalName == "si":
                        cur = new StringBuilder();
                        if (r.IsEmptyElement) { shared.Add(""); cur = null; }
                        break;
                    case XmlNodeType.Element when r.LocalName == "t": inText = !r.IsEmptyElement; break;
                    case XmlNodeType.Text or XmlNodeType.SignificantWhitespace when inText: cur?.Append(r.Value); break;
                    case XmlNodeType.EndElement when r.LocalName == "t": inText = false; break;
                    case XmlNodeType.EndElement when r.LocalName == "si": shared.Add(cur?.ToString() ?? ""); cur = null; break;
                }
            }
        }

        var sb = new StringBuilder();
        var sheets = zip.Entries
            .Where(e => e.FullName.StartsWith("xl/worksheets/sheet", StringComparison.OrdinalIgnoreCase) && e.FullName.EndsWith(".xml"))
            .OrderBy(e => e.FullName.Length).ThenBy(e => e.FullName);
        var n = 0;
        foreach (var sheet in sheets)
        {
            sb.Append("\n[Лист ").Append(++n).Append("]\n");
            using var s = sheet.Open();
            using var r = XmlReader.Create(s);
            var row = new List<string>();
            var cell = new StringBuilder();
            string? type = null;
            var inValue = false;
            var rows = 0;
            while (r.Read())
            {
                switch (r.NodeType)
                {
                    case XmlNodeType.Element when r.LocalName == "c": type = r.GetAttribute("t"); cell.Clear(); break;
                    case XmlNodeType.Element when r.LocalName is "v" || (r.LocalName == "t" && type == "inlineStr"): inValue = !r.IsEmptyElement; break;
                    case XmlNodeType.Text or XmlNodeType.SignificantWhitespace when inValue: cell.Append(r.Value); break;
                    case XmlNodeType.EndElement when r.LocalName is "v" or "t": inValue = false; break;
                    case XmlNodeType.EndElement when r.LocalName == "c":
                        var v = cell.ToString();
                        if (type == "s" && int.TryParse(v, out var idx) && idx < shared.Count) v = shared[idx];
                        if (v.Length > 0) row.Add(v);
                        break;
                    case XmlNodeType.EndElement when r.LocalName == "row":
                        if (row.Count > 0) sb.AppendJoin(" | ", row).Append('\n');
                        row.Clear();
                        rows++;
                        break;
                }
                if (rows > 3000) { sb.Append("… (листът е съкратен)\n"); break; }
            }
        }
        return sb.ToString();
    }

    private static string FromOdf(byte[] data)
    {
        using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
        var entry = zip.GetEntry("content.xml") ?? throw new InvalidDataException("липсва content.xml");
        using var s = entry.Open();
        using var r = XmlReader.Create(s, new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore });
        var sb = new StringBuilder();
        while (r.Read())
        {
            switch (r.NodeType)
            {
                case XmlNodeType.Text: sb.Append(r.Value); break;
                case XmlNodeType.Element when r.LocalName is "tab": sb.Append('\t'); break;
                case XmlNodeType.Element when r.LocalName is "line-break": sb.Append('\n'); break;
                case XmlNodeType.EndElement when r.LocalName is "p" or "h" or "table-row": sb.Append('\n'); break;
                case XmlNodeType.EndElement when r.LocalName is "table-cell": sb.Append(" | "); break;
            }
        }
        return sb.ToString();
    }

    // ---------- Архиви и подписи ----------

    private IEnumerable<ExtractedFile> FromArchive(string name, byte[] data, int depth)
    {
        var result = new List<ExtractedFile>();
        using var archive = ArchiveFactory.Open(new MemoryStream(data));
        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory))
        {
            if (entry.Size > 60L * 1024 * 1024) { result.Add(new ExtractedFile($"{name}/{entry.Key}", "пропуснат: твърде голям")); continue; }
            using var es = entry.OpenEntryStream();
            using var ms = new MemoryStream();
            es.CopyTo(ms);
            result.AddRange(Extract($"{name}/{entry.Key}", ms.ToArray(), depth + 1));
        }
        if (result.Count == 0) result.Add(new ExtractedFile(name, "празен архив"));
        return result;
    }

    private IEnumerable<ExtractedFile> FromSigned(string name, byte[] data, int depth)
    {
        var cms = new SignedCms();
        try
        {
            cms.Decode(data);
        }
        catch
        {
            // Понякога .p7m е Base64/PEM
            var text = Encoding.ASCII.GetString(data);
            var b64 = Pem().Replace(text, "").Replace("\r", "").Replace("\n", "");
            cms.Decode(Convert.FromBase64String(b64));
        }
        var content = cms.ContentInfo.Content;
        if (content is null || content.Length == 0) return [new ExtractedFile(name, "пропуснат: отделен подпис без съдържание")];
        var inner = Path.GetFileNameWithoutExtension(name); // „Спецификация.pdf.p7m“ → „Спецификация.pdf“
        return Extract(inner, content, depth + 1);
    }

    // ---------- Стари формати ----------

    /// <summary>
    /// DOC/XLS (OLE): текстът обикновено е записан като UTF-16LE. Вадим дълги последователности
    /// от букви, цифри и пунктуация. Не е перфектно, но запазва съдържанието.
    /// </summary>
    private static string FromOleHeuristic(byte[] data)
    {
        var sb = new StringBuilder();
        var run = new StringBuilder();
        void Flush()
        {
            if (run.Length >= 12) sb.Append(run).Append('\n');
            run.Clear();
        }
        for (var i = 0; i + 1 < data.Length; i += 2)
        {
            var ch = (char)(data[i] | (data[i + 1] << 8));
            if (char.IsLetterOrDigit(ch) || char.IsPunctuation(ch) || ch == ' ' || ch == '\t' || char.IsSymbol(ch)) run.Append(ch);
            else if (ch is '\r' or '\n' or '\v') { run.Append('\n'); }
            else Flush();
        }
        Flush();
        return sb.ToString();
    }

    private static string FromRtf(byte[] data)
    {
        var rtf = Encoding.ASCII.GetString(data);
        var sb = new StringBuilder();
        var bytes = new List<byte>();
        void FlushBytes() { if (bytes.Count > 0) { sb.Append(Cp1251.GetString(bytes.ToArray())); bytes.Clear(); } }

        var skipDepth = -1;
        var depth = 0;
        for (var i = 0; i < rtf.Length; i++)
        {
            var c = rtf[i];
            if (c == '{') { depth++; if (i + 2 < rtf.Length && rtf[i + 1] == '\\' && rtf[i + 2] == '*' && skipDepth < 0) skipDepth = depth; continue; }
            if (c == '}') { if (depth == skipDepth) skipDepth = -1; depth--; continue; }
            if (skipDepth >= 0) continue;
            if (c == '\\' && i + 1 < rtf.Length)
            {
                var n = rtf[i + 1];
                if (n == '\'' && i + 3 < rtf.Length) { bytes.Add(Convert.ToByte(rtf.Substring(i + 2, 2), 16)); i += 3; continue; }
                FlushBytes();
                if (n is '\\' or '{' or '}') { sb.Append(n); i++; continue; }
                var m = RtfWord().Match(rtf, i);
                if (m.Success)
                {
                    var word = m.Groups[1].Value;
                    if (word == "u" && int.TryParse(m.Groups[2].Value, out var code)) { sb.Append((char)(code < 0 ? code + 65536 : code)); i += m.Length - 1; if (i + 1 < rtf.Length && rtf[i + 1] == '?') i++; continue; }
                    if (word is "par" or "line" or "row") sb.Append('\n');
                    else if (word is "tab" or "cell") sb.Append('\t');
                    else if (word is "fonttbl" or "colortbl" or "stylesheet" or "info" or "pict") skipDepth = depth;
                    i += m.Length - 1;
                    continue;
                }
                continue;
            }
            if (c is '\r' or '\n') continue;
            FlushBytes();
            sb.Append(c);
        }
        FlushBytes();
        return sb.ToString();
    }

    // ---------- Помощни ----------

    private static ExtractedFile Text(string name, string text, bool approximate = false)
    {
        var t = Normalize(text);
        if (t.Length < 20) return new ExtractedFile(name, "без текст");
        return new ExtractedFile(name, approximate ? "ok (приблизително)" : "ok", t);
    }

    private static string Decode(byte[] data)
    {
        if (data.Length >= 3 && data[0] == 0xEF && data[1] == 0xBB && data[2] == 0xBF) return Encoding.UTF8.GetString(data, 3, data.Length - 3);
        var utf8 = new UTF8Encoding(false, true);
        try { return utf8.GetString(data); }
        catch (DecoderFallbackException) { return Cp1251.GetString(data); }
    }

    public static string Normalize(string s)
    {
        s = s.Replace("\r\n", "\n").Replace('\u00A0', ' ');
        s = Spaces().Replace(s, " ");
        s = BlankLines().Replace(s, "\n\n");
        return s.Trim();
    }

    private static bool StartsWith(byte[] data, string prefix)
    {
        if (data.Length < prefix.Length) return false;
        for (var i = 0; i < prefix.Length; i++) if (data[i] != prefix[i]) return false;
        return true;
    }

    private static bool HasZipEntry(byte[] data, string entry)
    {
        try
        {
            using var zip = new ZipArchive(new MemoryStream(data), ZipArchiveMode.Read);
            return zip.GetEntry(entry) is not null;
        }
        catch { return false; }
    }

    [GeneratedRegex(@"[ \t]+")] private static partial Regex Spaces();
    [GeneratedRegex(@"\n\s*\n(\s*\n)+")] private static partial Regex BlankLines();
    [GeneratedRegex(@"<[^>]+>")] private static partial Regex Tags();
    [GeneratedRegex(@"\p{L}")] private static partial Regex Letters();
    [GeneratedRegex(@"-----[A-Z0-9 ]+-----")] private static partial Regex Pem();
    [GeneratedRegex(@"\G\\([a-z]+)(-?\d+)? ?")] private static partial Regex RtfWord();
}
