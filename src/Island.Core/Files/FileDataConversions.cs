using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml;

namespace Island.Core.Files;

internal static class FileDataConversions
{
    public static IEnumerable<string[]> CsvRows(string text)
    {
        List<string> cells = []; var cell = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < text.Length; i++)
        {
            char ch = text[i];
            if (ch == '"')
            {
                if (quoted && i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                else quoted = !quoted;
            }
            else if (!quoted && ch == ',') { cells.Add(cell.ToString()); cell.Clear(); }
            else if (!quoted && (ch == '\r' || ch == '\n'))
            {
                if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                cells.Add(cell.ToString()); cell.Clear(); yield return cells.ToArray(); cells.Clear();
            }
            else cell.Append(ch);
        }
        if (quoted) throw new InvalidDataException("CSV com aspas sem fechamento.");
        if (cell.Length > 0 || cells.Count > 0) { cells.Add(cell.ToString()); yield return cells.ToArray(); }
    }

    public static void CsvToXlsx(string source, string target, CancellationToken token)
    {
        using var zip = ZipFile.Open(target, ZipArchiveMode.Create);
        void Entry(string name, string text) { using var w = new StreamWriter(zip.CreateEntry(name).Open(), Encoding.UTF8); w.Write(text); }
        Entry("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/worksheets/sheet1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/></Types>");
        Entry("_rels/.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
        Entry("xl/workbook.xml", "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><sheets><sheet name=\"Dados\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>");
        Entry("xl/_rels/workbook.xml.rels", "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>");
        using var xml = XmlWriter.Create(zip.CreateEntry("xl/worksheets/sheet1.xml").Open(), new XmlWriterSettings { Encoding = Encoding.UTF8, CloseOutput = true });
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        xml.WriteStartElement("worksheet", ns); xml.WriteStartElement("sheetData", ns);
        int rowIndex = 0;
        foreach (var row in CsvRows(System.IO.File.ReadAllText(source)))
        {
            token.ThrowIfCancellationRequested();
            if (++rowIndex > 1048576 || row.Length > 16384) throw new InvalidDataException("O CSV excede os limites de uma planilha XLSX.");
            xml.WriteStartElement("row", ns);
            foreach (string cell in row)
            {
                xml.WriteStartElement("c", ns); xml.WriteAttributeString("t", "inlineStr");
                xml.WriteStartElement("is", ns); xml.WriteStartElement("t", ns);
                xml.WriteAttributeString("xml", "space", null, "preserve");
                xml.WriteString(cell.Length <= 32767 ? cell : throw new InvalidDataException("Célula excede 32767 caracteres."));
                xml.WriteEndElement(); xml.WriteEndElement(); xml.WriteEndElement();
            }
            xml.WriteEndElement();
        }
        xml.WriteEndElement(); xml.WriteEndElement();
    }

    public static void JsonToCsv(string source, string target)
    {
        using var doc = JsonDocument.Parse(System.IO.File.ReadAllText(source));
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.EnumerateArray().Any(x => x.ValueKind != JsonValueKind.Object))
            throw new InvalidDataException("Use um array JSON de objetos: [{\"nome\":\"valor\"}].");
        var rows = doc.RootElement.EnumerateArray().ToArray();
        string[] keys = rows.SelectMany(r => r.EnumerateObject().Select(p => p.Name)).Distinct().ToArray();
        static string Quote(string s)
        {
            // Prevent spreadsheet formulas when a report is opened in Excel.
            if (s.TrimStart().FirstOrDefault() is '=' or '+' or '-' or '@' or '\t' or '\r') s = "'" + s;
            return "\"" + s.Replace("\"", "\"\"") + "\"";
        }
        using var writer = new StreamWriter(target, false, new UTF8Encoding(true));
        writer.WriteLine(string.Join(",", keys.Select(Quote)));
        foreach (var row in rows) writer.WriteLine(string.Join(",", keys.Select(k => Quote(row.TryGetProperty(k, out var v) ? v.ToString() : ""))));
    }
}
