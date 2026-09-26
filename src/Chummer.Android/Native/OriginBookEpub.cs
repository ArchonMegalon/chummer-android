using System.Globalization;
using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace Chummer.Android.Native;

/// <summary>Offline reading export, never a rules mutation or provider request.</summary>
internal static class OriginBookEpub
{
    // Retained first-party bytes only. The caller owns consent, provider
    // provenance and owner-bound storage; this exporter never fetches a URL.
    internal sealed record Illustration(string ChapterId, string TextDigest, string AltText, byte[] Bytes);
    private sealed record CapturedIllustration(string ChapterId, string Id, string Path, string MediaType,
        string TextDigest, string ImageDigest, string AltText, byte[] Bytes);
    private static readonly XNamespace Html = "http://www.w3.org/1999/xhtml";
    private static readonly XNamespace Opf = "http://www.idpf.org/2007/opf";
    private static readonly XNamespace Dc = "http://purl.org/dc/elements/1.1/";
    private static readonly XNamespace Epub = "http://www.idpf.org/2007/ops";

    internal static byte[] Create(RetainedOriginBook book, AndroidSurfaceCopy copy,
        IReadOnlyList<Illustration>? illustrations = null)
    {
        // Use the same selected edition as the visible reader. Pending prose,
        // owner IDs, workspace IDs, provider receipts and raw canon stay private.
        var chapters = book.Chapters.Select((chapter, index) => new
        {
            ChapterId = chapter.ChapterId,
            Id = $"chapter-{index + 1}",
            Title = book.IsOpeningSetup(chapter) ? copy["Origin.OpeningSetupTitle"] : chapter.Title,
            Text = book.ChapterText(chapter)
        }).ToArray();
        var pictures = CaptureIllustrations(book, illustrations ?? book.SceneExports());
        string language;
        try { language = CultureInfo.GetCultureInfo(book.Locale).Name; }
        catch (CultureNotFoundException) { language = "en"; }
        if (string.IsNullOrWhiteSpace(language)) language = "en";
        string identifier = "urn:sha256:" + Convert.ToHexString(SHA256.HashData(
            System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(new { book.RunnerName, language,
                chapters = chapters.Select(c => new { c.Id, c.Title, c.Text }),
                pictures = pictures.Select(p => new { Chapter = chapters.Single(c => c.ChapterId == p.ChapterId).Id,
                    p.Id, p.ImageDigest, p.AltText, p.TextDigest }) }))).ToLowerInvariant();

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
            Write(zip, "EPUB/style.css", "body{font-family:serif;line-height:1.65;margin:5%;overflow-wrap:anywhere}h1,h2{line-height:1.25}p{margin:0 0 1em;white-space:pre-wrap}figure{margin:1em 0;break-inside:avoid}img{max-width:100%;height:auto}nav ol{line-height:1.7}");
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
                    new XElement(Html + "h1", Text(chapter.Title)),
                    pictures.Where(p => p.ChapterId == chapter.ChapterId).Select(p =>
                        new XElement(Html + "figure", new XElement(Html + "img",
                            new XAttribute("src", p.Path), new XAttribute("alt", Text(p.AltText))))), paragraphs));
            }
            foreach (var picture in pictures)
            {
                using var entry = zip.CreateEntry("EPUB/" + picture.Path, CompressionLevel.NoCompression).Open();
                entry.Write(picture.Bytes);
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
                        new XAttribute("href", chapter.Id + ".xhtml"), new XAttribute("media-type", "application/xhtml+xml"))),
                    pictures.Select(p => new XElement(Opf + "item", new XAttribute("id", p.Id),
                        new XAttribute("href", p.Path), new XAttribute("media-type", p.MediaType)))),
                new XElement(Opf + "spine", new XElement(Opf + "itemref", new XAttribute("idref", "title")),
                    chapters.Select(chapter => new XElement(Opf + "itemref", new XAttribute("idref", chapter.Id))))));
        }
        return output.ToArray();
    }

    private static CapturedIllustration[] CaptureIllustrations(RetainedOriginBook book, IReadOnlyList<Illustration>? source)
    {
        if (source is null) return [];
        if (source.Count > 8) throw new InvalidDataException("Too many book illustrations.");
        var result = new List<CapturedIllustration>();
        long total = 0;
        foreach (var picture in source)
        {
            if (picture is null || picture.Bytes is not { Length: > 0 and <= 4 * 1024 * 1024 }
                || (total += picture.Bytes.Length) > 16 * 1024 * 1024
                || string.IsNullOrWhiteSpace(picture.AltText) || picture.AltText.Length > 1024)
                throw new InvalidDataException("The book illustration is invalid or oversized.");
            var chapter = book.Chapters.SingleOrDefault(c => c.ChapterId == picture.ChapterId);
            if (chapter is null || picture.TextDigest != Hash(Encoding.UTF8.GetBytes(book.ChapterText(chapter))))
                throw new InvalidDataException("The illustration does not match the selected chapter text.");
            byte[] captured = picture.Bytes.ToArray();
            string mime = ImageType(captured);
            string id = "scene-" + (result.Count + 1);
            result.Add(new(picture.ChapterId, id, "images/" + id + (mime == "image/png" ? ".png" : ".jpg"),
                mime, picture.TextDigest, Hash(captured), picture.AltText, captured));
        }
        return result.ToArray();
    }

    private static string Hash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    internal static string ImageType(ReadOnlySpan<byte> bytes)
    {
        // Only inert raster formats with bounded dimensions; no SVG, HTML,
        // data URLs or archive paths supplied by a provider can reach the EPUB.
        static bool Dimensions(uint width, uint height) => width is > 0 and <= 4096 && height is > 0 and <= 4096;
        if (bytes.Length >= 45 && bytes[..8].SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })
            && BinaryPrimitives.ReadUInt32BigEndian(bytes[8..]) == 13 && bytes.Slice(12, 4).SequenceEqual("IHDR"u8)
            && bytes.Slice(bytes.Length - 8, 4).SequenceEqual("IEND"u8)
            && Dimensions(BinaryPrimitives.ReadUInt32BigEndian(bytes[16..]), BinaryPrimitives.ReadUInt32BigEndian(bytes[20..])))
            return "image/png";
        if (bytes.Length > 4 && bytes[0] == 0xff && bytes[1] == 0xd8 && bytes[^2] == 0xff && bytes[^1] == 0xd9)
        {
            int position = 2;
            while (position + 3 < bytes.Length)
            {
                if (bytes[position++] != 0xff) break;
                while (position < bytes.Length && bytes[position] == 0xff) position++;
                if (position + 2 >= bytes.Length) break;
                byte marker = bytes[position++];
                if (marker is 0xda or 0xd9) break;
                int length = BinaryPrimitives.ReadUInt16BigEndian(bytes[position..]);
                if (length < 2 || position + length > bytes.Length) break;
                if (marker is 0xc0 or 0xc1 or 0xc2 && length >= 8
                    && Dimensions(BinaryPrimitives.ReadUInt16BigEndian(bytes[(position + 5)..]),
                        BinaryPrimitives.ReadUInt16BigEndian(bytes[(position + 3)..]))) return "image/jpeg";
                position += length;
            }
        }
        throw new InvalidDataException("The illustration is not a bounded PNG or JPEG.");
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
