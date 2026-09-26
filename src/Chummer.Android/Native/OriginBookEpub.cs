using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Android.Native;

/// <summary>Offline reading export, never a rules mutation or provider request.</summary>
internal static class OriginBookEpub
{
    private static readonly XNamespace Html = "http://www.w3.org/1999/xhtml";
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Epub = "http://www.idpf.org/2007/ops";

    internal static byte[] Create(RetainedOriginBook book, AndroidSurfaceCopy copy)
    {
        // Use the same selected edition as the visible reader. Pending prose,
        // owner IDs, workspace IDs, provider receipts and raw canon stay private.
        var chapters = book.Chapters.Select((chapter, index) => new
        {
            Id = $"chapter-{index + 1}",
            Title = book.IsOpeningSetup(chapter) ? copy["Origin.OpeningSetupTitle"] : chapter.Title,
            Text = book.ChapterText(chapter)
        }).ToArray();
        string language;
        try { language = CultureInfo.GetCultureInfo(book.Locale).Name; }
        catch (CultureNotFoundException) { language = "en"; }
        if (string.IsNullOrWhiteSpace(language)) language = "en";
        string identifier = "urn:sha256:" + Convert.ToHexString(SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { book.RunnerName, language, chapters }))).ToLowerInvariant();

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            // EPUB requires this first entry, uncompressed, with no BOM.
            Write(zip, "mimetype", "application/epub+zip", CompressionLevel.NoCompression);
            XNamespace container = "urn:oasis:names:tc:opendocument:xmlns:container";
            WriteXml(zip, "META-INF/container.xml", new XElement(container + "container",
                new XAttribute("version", "1.0"), new XElement(container + "rootfiles",
                    new XElement(container + "rootfile", new XAttribute("full-path", "EPUB/package.opf"),
                        new XAttribute("media-type", "application/oebps-package+xml")))));
            Write(zip, "EPUB/style.css", "body{font-family:serif;line-height:1.65;margin:5%;overflow-wrap:anywhere}h1,h2{line-height:1.25}p{margin:0 0 1em;white-space:pre-wrap}img{max-width:100%;height:auto}nav ol{line-height:1.7}");
            WriteXml(zip, "EPUB/title.xhtml", Page(book.RunnerName, language,
                new XElement(Html + "h1", Text(book.RunnerName)),
                new XElement(Html + "p", Text(copy["Origin.BookSavedChapters"])),
                new XElement(Html + "p", Text(copy.Format("Origin.BookMetadata", "chummer.run")))));
            foreach (var chapter in chapters)
            {
                var paragraphs = chapter.Text.Replace("\r\n", "\n").Replace('\r', '\n')
                    .Split("\n\n", StringSplitOptions.RemoveEmptyEntries)
                    .Select(p => new XElement(Html + "p", Text(p)));
                WriteXml(zip, $"EPUB/{chapter.Id}.xhtml", Page(chapter.Title, language,
                    new XElement(Html + "h1", Text(chapter.Title)), paragraphs));
            }
            WriteXml(zip, "EPUB/nav.xhtml", Page(book.RunnerName, language,
                new XElement(Html + "nav", new XAttribute(Epub + "type", "toc"),
                    new XElement(Html + "h1", Text(book.RunnerName)),
                    new XElement(Html + "ol", chapters.Select(chapter => new XElement(Html + "li",
                        new XElement(Html + "a", new XAttribute("href", chapter.Id + ".xhtml"), Text(chapter.Title))))))));
            WriteXml(zip, "EPUB/package.opf", new XElement(Opf + "package",
                new XAttribute("version", "3.0"), new XAttribute("unique-identifier", "bookid"),
                new XElement(Opf + "metadata",
                    new XElement(Dc + "identifier", new XAttribute("id", "bookid"), identifier),
                    new XElement(Dc + "title", Text(book.RunnerName)),
                    new XElement(Dc + "language", language), new XElement(Dc + "creator", "chummer.run"),
                    new XElement(Opf + "meta", new XAttribute("property", "dcterms:modified"),
                        DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture))),
                new XElement(Opf + "manifest",
                    new XElement(Opf + "item", new XAttribute("id", "nav"), new XAttribute("href", "nav.xhtml"),
                        new XAttribute("media-type", "application/xhtml+xml"), new XAttribute("properties", "nav")),
                    new XElement(Opf + "item", new XAttribute("id", "style"), new XAttribute("href", "style.css"), new XAttribute("media-type", "text/css")),
                    new XElement(Opf + "item", new XAttribute("id", "title"), new XAttribute("href", "title.xhtml"), new XAttribute("media-type", "application/xhtml+xml")),
                    chapters.Select(chapter => new XElement(Opf + "item", new XAttribute("id", chapter.Id),
                        new XAttribute("href", chapter.Id + ".xhtml"), new XAttribute("media-type", "application/xhtml+xml")))),
                new XElement(Opf + "spine", new XElement(Opf + "itemref", new XAttribute("idref", "title")),
                    chapters.Select(chapter => new XElement(Opf + "itemref", new XAttribute("idref", chapter.Id))))));
        }
        return output.ToArray();
    }

    private static XElement Page(string title, string language, params object[] content)
        => new(Html + "html", new XAttribute("lang", language), new XAttribute(XNamespace.Xml + "lang", language),
            new XElement(Html + "head", new XElement(Html + "title", Text(title)),
                new XElement(Html + "link", new XAttribute("rel", "stylesheet"), new XAttribute("type", "text/css"), new XAttribute("href", "style.css"))),
            new XElement(Html + "body", content));

    private static string Text(string value)
    {
        // Keep valid non-BMP characters (emoji included), replacing only XML-
        // forbidden control/surrogate input. Never interpret prose as markup.
        var result = new StringBuilder(value.Length);
        foreach (var rune in value.EnumerateRunes())
            result.Append(rune.Value > 0xffff || XmlConvert.IsXmlChar((char)rune.Value) ? rune.ToString() : "\uFFFD");
        return result.ToString();
    }

    private static void WriteXml(ZipArchive zip, string path, XElement root)
        => Write(zip, path, "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n" + root.ToString(SaveOptions.DisableFormatting));

    private static void Write(ZipArchive zip, string path, string content, CompressionLevel compression = CompressionLevel.Optimal)
    {
        using var entry = zip.CreateEntry(path, compression).Open();
        using var writer = new StreamWriter(entry, new UTF8Encoding(false));
        writer.Write(content);
    }
}
