using System.IO;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace OpenBoardExporter;

public sealed class PdfPageRenderer
{
    private readonly CoreWebView2 _webView;
    private readonly string _tempRoot;
    private readonly OpenBoardSvgPreprocessor _preprocessor;

    public PdfPageRenderer(CoreWebView2 webView, string tempRoot)
    {
        _webView = webView;
        _tempRoot = tempRoot;
        _preprocessor = new OpenBoardSvgPreprocessor(tempRoot);
    }

    public async Task RenderPageAsync(
        string svgPath,
        string documentRoot,
        string outputPdf)
    {
        var processedSvg = await _preprocessor.ProcessAsync(svgPath, documentRoot);

        var pageSize = SvgPageSize.Parse(processedSvg);
        var htmlPath = Path.Combine(
            _tempRoot,
            "html",
            Guid.NewGuid().ToString("N") + ".html");

        Directory.CreateDirectory(Path.GetDirectoryName(htmlPath)!);

        var html = HtmlDocumentBuilder.Build(processedSvg, pageSize);
        await File.WriteAllTextAsync(htmlPath, html, new UTF8Encoding(false));

        var uri = new Uri(Path.GetFullPath(htmlPath)).AbsoluteUri;

        await NavigateAsync(uri);

        var settings = _webView.Environment.CreatePrintSettings();
        settings.ShouldPrintBackgrounds = true;
        settings.ShouldPrintHeaderAndFooter = false;
        settings.MarginTop = 0;
        settings.MarginBottom = 0;
        settings.MarginLeft = 0;
        settings.MarginRight = 0;
        settings.PageWidth = pageSize.WidthInches;
        settings.PageHeight = pageSize.HeightInches;
        settings.ScaleFactor = 1.0;

        await _webView.PrintToPdfAsync(outputPdf, settings);

        try { File.Delete(htmlPath); } catch { }
    }

    private async Task NavigateAsync(string uri)
    {
        var tcs = new TaskCompletionSource<bool>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        void Handler(object? sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _webView.NavigationCompleted -= Handler;

            if (e.IsSuccess)
                tcs.TrySetResult(true);
            else
                tcs.TrySetException(
                    new InvalidOperationException(
                        $"WebView2 konnte die Seite nicht laden: {e.WebErrorStatus}"));
        }

        _webView.NavigationCompleted += Handler;
        _webView.Navigate(uri);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(60));
    }
}
