using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Pdfium.Net.Native.Enums;
using Pdfium.Net.Wrapper;

namespace OpenBoardExporter;

public sealed class OpenBoardSvgPreprocessor
{
    private readonly string _tempRoot;
    private readonly Dictionary<string, string> _imageSvgCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _pdfPageCache = new(StringComparer.OrdinalIgnoreCase);

    public OpenBoardSvgPreprocessor(string tempRoot)
    {
        _tempRoot = tempRoot;
    }

    public async Task<string> ProcessAsync(string svgPath, string documentRoot)
    {
        var svg = await File.ReadAllTextAsync(svgPath, Encoding.UTF8);
        return await ProcessSvgTextAsync(svg, documentRoot, Path.GetDirectoryName(svgPath)!);
    }

    private async Task<string> ProcessSvgTextAsync(
        string svg,
        string documentRoot,
        string currentDirectory)
    {
        svg = ReplaceEmbeddedPdfForeignObjects(svg, documentRoot);
        svg = await ReplaceNestedSvgImagesAsync(svg, documentRoot, currentDirectory);
        svg = ReplaceOpenBoardTextObjects(svg);
        svg = AddOpenBoardBackground(svg);

        return svg;
    }

    private string ReplaceEmbeddedPdfForeignObjects(
        string svg,
        string documentRoot)
    {
        var pattern = new Regex(
            @"<foreignObject\b(?<attrs>[^>]*?)(?:/>|>.*?</foreignObject>)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return pattern.Replace(svg, match =>
        {
            var full = match.Value;

            if (!full.Contains("requiredExtensions=\"http://ns.adobe.com/pdf/1.3/\"",
                    StringComparison.OrdinalIgnoreCase) &&
                !full.Contains("requiredExtensions='http://ns.adobe.com/pdf/1.3/'",
                    StringComparison.OrdinalIgnoreCase))
            {
                return full;
            }

            var href = GetAttribute(full, "xlink:href") ?? GetAttribute(full, "href");
            if (href is null)
                return full;

            var hash = href.IndexOf('#');
            var objectRef = hash >= 0 ? href[..hash] : href;
            var pageRef = hash >= 0 ? href[(hash + 1)..] : "page=1";

            if (!objectRef.StartsWith("objects/", StringComparison.OrdinalIgnoreCase))
                return full;

            var pageNumber = ParsePageNumber(pageRef);
            var pdfPath = Path.Combine(
                documentRoot,
                objectRef.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(pdfPath))
            {
                Console.Error.WriteLine($"      [WARN] Eingebettete PDF fehlt: {objectRef}");
                return full;
            }

            try
            {
                var key = $"{pdfPath}|{pageNumber}";
                if (!_pdfPageCache.TryGetValue(key, out var dataUri))
                {
                    var pngPath = RenderPdfPageToPng(pdfPath, pageNumber);
                    var bytes = File.ReadAllBytes(pngPath);
                    dataUri = "data:image/png;base64," + Convert.ToBase64String(bytes);
                    _pdfPageCache[key] = dataUri;

                    try { File.Delete(pngPath); } catch { }
                }

                var attrs = match.Groups["attrs"].Value;
                attrs = RemoveAttribute(attrs, "xlink:href");
                attrs = RemoveAttribute(attrs, "href");
                attrs = RemoveAttribute(attrs, "requiredExtensions");

                return $"<image{attrs} href=\"{dataUri}\" preserveAspectRatio=\"none\" />";
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"      [WARN] PDF-Objekt konnte nicht gerendert werden: {objectRef}#{pageNumber}: {ex.Message}");
                return full;
            }
        });
    }

    private async Task<string> ReplaceNestedSvgImagesAsync(
        string svg,
        string documentRoot,
        string currentDirectory)
    {
        var pattern = new Regex(
            @"<(?:image)\b(?<attrs>[^>]*?)(?:/>|>.*?</image>)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        var matches = pattern.Matches(svg);
        if (matches.Count == 0)
            return svg;

        var sb = new StringBuilder(svg.Length);

        int last = 0;
        foreach (Match match in matches)
        {
            sb.Append(svg, last, match.Index - last);

            var full = match.Value;
            var href = GetAttribute(full, "xlink:href") ?? GetAttribute(full, "href");

            if (href is null || !href.StartsWith("images/", StringComparison.OrdinalIgnoreCase))
            {
                sb.Append(full);
                last = match.Index + match.Length;
                continue;
            }

            var nestedPath = Path.Combine(
                documentRoot,
                href.Replace('/', Path.DirectorySeparatorChar));

            if (!File.Exists(nestedPath))
            {
                sb.Append(full);
                last = match.Index + match.Length;
                continue;
            }

            try
            {
                var normalized = Path.GetFullPath(nestedPath);

                if (!_imageSvgCache.TryGetValue(normalized, out var dataUri))
                {
                    var nestedSvg = await File.ReadAllTextAsync(normalized, Encoding.UTF8);
                    nestedSvg = await ProcessSvgTextAsync(
                        nestedSvg,
                        documentRoot,
                        Path.GetDirectoryName(normalized)!);

                    dataUri = "data:image/svg+xml;base64," +
                              Convert.ToBase64String(Encoding.UTF8.GetBytes(nestedSvg));

                    _imageSvgCache[normalized] = dataUri;
                }

                var attrs = match.Groups["attrs"].Value;
                attrs = RemoveAttribute(attrs, "xlink:href");
                attrs = RemoveAttribute(attrs, "href");

                sb.Append($"<image{attrs} href=\"{dataUri}\" />");
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(
                    $"      [WARN] Eingebettetes SVG konnte nicht verarbeitet werden: {href}: {ex.Message}");
                sb.Append(full);
            }

            last = match.Index + match.Length;
        }

        sb.Append(svg, last, svg.Length - last);
        return sb.ToString();
    }

    private static string ReplaceOpenBoardTextObjects(string svg)
    {
        var pattern = new Regex(
            @"<foreignObject\b(?<attrs>[^>]*?)(?:>(?<content>.*?)</foreignObject>|/>)",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return pattern.Replace(svg, match =>
        {
            var full = match.Value;

            if (!full.Contains("ub:type=\"text\"", StringComparison.OrdinalIgnoreCase) &&
                !full.Contains("ub:type='text'", StringComparison.OrdinalIgnoreCase))
            {
                return full;
            }

            var contentMatch = Regex.Match(
                full,
                @"<itemTextContent>(?<content>.*?)</itemTextContent>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!contentMatch.Success)
                return full;

            var decoded = WebUtility.HtmlDecode(contentMatch.Groups["content"].Value);

            var bodyMatch = Regex.Match(
                decoded,
                @"<body\b[^>]*>(?<body>.*?)</body>",
                RegexOptions.IgnoreCase | RegexOptions.Singleline);

            if (!bodyMatch.Success)
                return full;

            var body = bodyMatch.Groups["body"].Value;

            // Qt's rich-text CSS contains properties browsers do not know.
            // They are harmless, but remove the Qt-only block indentation rules.
            body = body.Replace("-qt-block-indent:0;", "", StringComparison.OrdinalIgnoreCase)
                       .Replace("-qt-paragraph-type:empty;", "", StringComparison.OrdinalIgnoreCase);

            var attrs = match.Groups["attrs"].Value;
            attrs = RemoveAttribute(attrs, "ub:type");

            return $"<foreignObject{attrs}><div xmlns=\"http://www.w3.org/1999/xhtml\" style=\"width:100%;height:100%;overflow:hidden;\">{body}</div></foreignObject>";
        });
    }

    private static string AddOpenBoardBackground(string svg)
    {
        try
        {
            var root = XElement.Parse(svg, LoadOptions.PreserveWhitespace);
            XNamespace ub = "http://uniboard.mnemis.com/document";

            var viewBox = root.Attribute("viewBox")?.Value?
                .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

            if (viewBox is null || viewBox.Length != 4)
                return svg;

            if (!double.TryParse(viewBox[0], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var x) ||
                !double.TryParse(viewBox[1], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var y) ||
                !double.TryParse(viewBox[2], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var w) ||
                !double.TryParse(viewBox[3], System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var h))
                return svg;

            bool crossed = ParseBool(root.Attribute(ub + "crossed-background")?.Value);
            bool ruled = ParseBool(root.Attribute(ub + "ruled-background")?.Value);
            bool grid = ParseBool(root.Attribute(ub + "grid-background")?.Value);

            if (!crossed && !ruled && !grid)
                return svg;

            var gridSize = 41.0;
            double.TryParse(
                root.Attribute(ub + "grid-size")?.Value,
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out gridSize);

            if (gridSize <= 0)
                gridSize = 41;

            var ns = root.Name.Namespace;

            var defs = new XElement(ns + "defs");
            var pattern = new XElement(
                ns + "pattern",
                new XAttribute("id", "axiom-openboard-background"),
                new XAttribute("patternUnits", "userSpaceOnUse"),
                new XAttribute("width", gridSize.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XAttribute("height", gridSize.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XAttribute("x", x.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XAttribute("y", y.ToString(System.Globalization.CultureInfo.InvariantCulture)));

            if (grid || crossed)
            {
                pattern.Add(
                    new XElement(ns + "path",
                        new XAttribute("d", $"M {gridSize} 0 L 0 0 0 {gridSize}"),
                        new XAttribute("fill", "none"),
                        new XAttribute("stroke", "#d7d7d7"),
                        new XAttribute("stroke-width", "1")));
            }

            if (ruled)
            {
                pattern.Add(
                    new XElement(ns + "path",
                        new XAttribute("d", $"M 0 {gridSize - 1} L {gridSize} {gridSize - 1}"),
                        new XAttribute("fill", "none"),
                        new XAttribute("stroke", "#c8d7ef"),
                        new XAttribute("stroke-width", "1")));
            }

            if (crossed)
            {
                var mid = gridSize / 2.0;
                pattern.Add(
                    new XElement(ns + "path",
                        new XAttribute("d", $"M {mid - 4} {mid} H {mid + 4} M {mid} {mid - 4} V {mid + 4}"),
                        new XAttribute("stroke", "#d7d7d7"),
                        new XAttribute("stroke-width", "1")));
            }

            defs.Add(pattern);

            var background = new XElement(
                ns + "rect",
                new XAttribute("x", x.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XAttribute("y", y.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XAttribute("width", w.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XAttribute("height", h.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                new XAttribute("fill", "url(#axiom-openboard-background)"));

            root.AddFirst(background);
            root.AddFirst(defs);

            return root.ToString(SaveOptions.DisableFormatting);
        }
        catch
        {
            return svg;
        }
    }

    private string RenderPdfPageToPng(string pdfPath, int pageNumber)
    {
        var output = Path.Combine(
            _tempRoot,
            "pdf-cache",
            Guid.NewGuid().ToString("N") + ".png");

        Directory.CreateDirectory(Path.GetDirectoryName(output)!);

        using var doc = PdfDocument.Load(pdfPath);

        if (pageNumber < 1 || pageNumber > doc.Pages.Count)
            throw new ArgumentOutOfRangeException(
                nameof(pageNumber),
                $"PDF hat {doc.Pages.Count} Seiten, angefordert wurde {pageNumber}.");

        var page = doc.Pages[pageNumber - 1];

        var width = Math.Max(1, (int)Math.Round(page.Width / 72.0 * 144.0));
        var height = Math.Max(1, (int)Math.Round(page.Height / 72.0 * 144.0));

        using var bitmap = new PdfBitmap(width, height, PdfBitmapFormats.BGRA, IntPtr.Zero, 0);
        page.RenderPage(bitmap, 0, 0, width, height, PdfRotation.Rotate0, PdfRenderFlags.LcdText);

        using var stream = File.Create(output);
        using var png = bitmap.AsStream(width, height);
        png.CopyTo(stream);
        return output;
    }

    private static int ParsePageNumber(string fragment)
    {
        var match = Regex.Match(fragment, @"page\s*=\s*(\d+)", RegexOptions.IgnoreCase);
        return match.Success && int.TryParse(match.Groups[1].Value, out var page)
            ? page
            : 1;
    }

    private static string? GetAttribute(string text, string name)
    {
        var match = Regex.Match(
            text,
            $@"\b{Regex.Escape(name)}\s*=\s*[""'](?<value>.*?)[""']",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);

        return match.Success ? WebUtility.HtmlDecode(match.Groups["value"].Value) : null;
    }

    private static string RemoveAttribute(string text, string name)
    {
        return Regex.Replace(
            text,
            $@"\s+{Regex.Escape(name)}\s*=\s*[""'].*?[""']",
            "",
            RegexOptions.IgnoreCase | RegexOptions.Singleline);
    }

    private static bool ParseBool(string? value)
        => value is not null &&
           (value.Equals("true", StringComparison.OrdinalIgnoreCase) ||
            value == "1");
}
