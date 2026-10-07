using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Pdfium.Net.Wrapper;

namespace OpenBoardExporter;

public sealed class UbxExporter
{
    private readonly PdfPageRenderer[] _renderers;
    private readonly string _tempRoot;

    public UbxExporter(PdfPageRenderer[] renderers, string tempRoot)
    {
        if (renderers.Length == 0)
            throw new ArgumentException("Mindestens ein PDF-Renderer ist erforderlich.", nameof(renderers));

        _renderers = renderers;
        _tempRoot = tempRoot;
    }

    public async Task ExportAsync(
        string ubxPath,
        string outputRoot,
        IProgress<int>? progress = null,
        Action<string>? log = null)
    {
        Console.WriteLine();
        Console.WriteLine($"=== {Path.GetFileName(ubxPath)} ===");
        log?.Invoke($"=== {Path.GetFileName(ubxPath)} ===");

        var archiveRoot = Path.Combine(_tempRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(archiveRoot);

        ZipFile.ExtractToDirectory(ubxPath, archiveRoot);

        var documentRoots = Directory.EnumerateDirectories(archiveRoot)
            .Where(IsOpenBoardDocumentDirectory)
            .OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (documentRoots.Count == 0)
            throw new InvalidDataException($"Keine OpenBoard-Dokumente in {ubxPath} gefunden.");

        Console.WriteLine($"Dokumente: {documentRoots.Count}");
        log?.Invoke($"Dokumente gefunden: {documentRoots.Count}");

        int index = 0;
        var failures = new List<Exception>();
        foreach (var documentRoot in documentRoots)
        {
            index++;

            try
            {
                await ExportDocumentAsync(
                    documentRoot,
                    outputRoot,
                    index,
                    documentRoots.Count,
                    progress,
                    log);
            }
            catch (Exception ex)
            {
                var message = $"Dokument '{Path.GetFileName(documentRoot)}' fehlgeschlagen: {ex.Message}";
                Console.Error.WriteLine($"[FEHLER] {message}");
                log?.Invoke($"FEHLER: {message}");
                failures.Add(new InvalidOperationException(message, ex));
            }
        }

        if (failures.Count > 0)
            throw new AggregateException($"{failures.Count} Dokument(e) konnten nicht exportiert werden.", failures);
    }

    private async Task ExportDocumentAsync(
        string documentRoot,
        string outputRoot,
        int documentIndex,
        int documentCount,
        IProgress<int>? progress = null,
        Action<string>? log = null)
    {
        var metadataPath = Path.Combine(documentRoot, "metadata.rdf");
        if (!File.Exists(metadataPath))
            throw new InvalidDataException("metadata.rdf fehlt.");

        var metadata = ReadMetadata(metadataPath);
        var pageFiles = Directory.EnumerateFiles(documentRoot, "page*.svg", SearchOption.TopDirectoryOnly)
            .OrderBy(GetPageNumber)
            .ToList();

        if (pageFiles.Count == 0)
            throw new InvalidDataException("Keine page*.svg-Dateien gefunden.");

        var relativeFolder = GetOutputFolder(metadata.DocumentType);
        var folder = Path.Combine(outputRoot, relativeFolder);
        Directory.CreateDirectory(folder);

        var fileName = SanitizeFileName(RemoveKnownExtensions(metadata.Title)) + ".pdf";
        if (string.IsNullOrWhiteSpace(fileName) || fileName == ".pdf")
            fileName = $"Document-{documentIndex}.pdf";

        var outputPath = GetUniquePath(Path.Combine(folder, fileName));

        Console.WriteLine(
            $"[{documentIndex}/{documentCount}] {metadata.Title} -> {Path.GetRelativePath(outputRoot, outputPath)}");
        log?.Invoke($"Dokument {documentIndex}/{documentCount}: {metadata.Title}");

        var pagePdfs = new string[pageFiles.Count];

        try
        {
            var completedPages = 0;
            var workerCount = Math.Min(_renderers.Length, pageFiles.Count);
            var workers = Enumerable.Range(0, workerCount)
                .Select(workerIndex => RenderPagesAsync(workerIndex))
                .ToArray();

            await Task.WhenAll(workers);
            MergePdfPages(pagePdfs, outputPath);

            if (progress is not null)
            {
                var percent = (int)Math.Round(documentIndex * 100.0 / documentCount);
                progress.Report(Math.Clamp(percent, 0, 100));
            }

            async Task RenderPagesAsync(int workerIndex)
            {
                for (int i = workerIndex; i < pageFiles.Count; i += workerCount)
                {
                    Console.WriteLine($"    Seite {i + 1}/{pageFiles.Count}");
                    log?.Invoke($"  Seite {i + 1}/{pageFiles.Count}");

                    var pagePdf = Path.Combine(
                        _tempRoot,
                        "pages",
                        Guid.NewGuid().ToString("N") + ".pdf");
                    Directory.CreateDirectory(Path.GetDirectoryName(pagePdf)!);

                    await _renderers[workerIndex].RenderPageAsync(
                        pageFiles[i],
                        documentRoot,
                        pagePdf);

                    pagePdfs[i] = pagePdf;

                    if (progress is not null)
                    {
                        var finished = Interlocked.Increment(ref completedPages);
                        var documentBase = (documentIndex - 1) * 100.0 / documentCount;
                        var pagePortion = (100.0 / documentCount) / pageFiles.Count;
                        var percent = (int)Math.Round(documentBase + finished * pagePortion);
                        progress.Report(Math.Clamp(percent, 0, 100));
                    }
                }
            }
        }
        finally
        {
            foreach (var pagePdf in pagePdfs)
            {
                if (pagePdf is not null)
                {
                    try { File.Delete(pagePdf); } catch { }
                }
            }
        }
    }

    private static bool IsOpenBoardDocumentDirectory(string path)
        => File.Exists(Path.Combine(path, "metadata.rdf")) &&
           Directory.EnumerateFiles(path, "page*.svg", SearchOption.TopDirectoryOnly).Any();

    private static OpenBoardMetadata ReadMetadata(string path)
    {
        var doc = XDocument.Load(path);
        XNamespace dc = "http://purl.org/dc/elements/1.1/";

        var description = doc.Descendants()
            .FirstOrDefault(x => x.Name.LocalName == "Description")
            ?? throw new InvalidDataException($"Ungültige metadata.rdf: {path}");

        var title = description.Elements(dc + "title").FirstOrDefault()?.Value
                    ?? "OpenBoard Document";

        var type = description.Elements(dc + "type").FirstOrDefault()?.Value
                   ?? "MyDocuments";

        return new OpenBoardMetadata(title, type);
    }

    private static string GetOutputFolder(string documentType)
    {
        var normalized = documentType.Replace('\\', '/').Trim('/');

        const string prefix = "MyDocuments/";
        if (normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            normalized = normalized[prefix.Length..];

        if (string.IsNullOrWhiteSpace(normalized))
            return string.Empty;

        var parts = normalized.Split(
            '/',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        return Path.Combine(parts.Select(SanitizeFileName).ToArray());
    }

    private static string RemoveKnownExtensions(string name)
    {
        var result = name.Trim();

        while (true)
        {
            var lower = result.ToLowerInvariant();

            if (lower.EndsWith(".crdownload"))
                result = result[..^11];
            else if (lower.EndsWith(".download"))
                result = result[..^9];
            else if (lower.EndsWith(".ubz"))
                result = result[..^4];
            else if (lower.EndsWith(".ubx"))
                result = result[..^4];
            else if (lower.EndsWith(".pdf"))
                result = result[..^4];
            else
                return result.Trim();
        }
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(value.Length);

        foreach (var c in value)
            sb.Append(invalid.Contains(c) ? '_' : c);

        var result = sb.ToString().Trim().TrimEnd('.');

        if (string.IsNullOrWhiteSpace(result))
            return "Unbenannt";

        return result;
    }

    private static int GetPageNumber(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        var digits = new string(name.Where(char.IsDigit).ToArray());

        return int.TryParse(digits, out var number) ? number : int.MaxValue;
    }

    private static string GetUniquePath(string path)
    {
        if (!File.Exists(path))
            return path;

        var directory = Path.GetDirectoryName(path)!;
        var name = Path.GetFileNameWithoutExtension(path);
        var ext = Path.GetExtension(path);

        for (int i = 2; ; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){ext}");
            if (!File.Exists(candidate))
                return candidate;
        }
    }

    private static void MergePdfPages(IReadOnlyList<string> pages, string outputPath)
    {
        if (pages.Count == 0)
            throw new InvalidOperationException("Keine PDF-Seiten zum Zusammenführen.");

        var merged = PdfDocument.MergePage(pages.ToArray());
        merged.Save(outputPath);
    }

    private sealed record OpenBoardMetadata(string Title, string DocumentType);
}
