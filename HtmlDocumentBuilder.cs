using System.Globalization;
using System.Text;

namespace OpenBoardExporter;

public static class HtmlDocumentBuilder
{
    public static string Build(string svg, SvgPageSize size)
    {
        var width = size.WidthInches.ToString("0.######", CultureInfo.InvariantCulture);
        var height = size.HeightInches.ToString("0.######", CultureInfo.InvariantCulture);

        var builder = new StringBuilder();
        builder.AppendLine("<!doctype html>");
        builder.AppendLine("<html>");
        builder.AppendLine("<head>");
        builder.AppendLine("<meta charset=\"utf-8\">");
        builder.AppendLine("<style>");
        builder.AppendLine("@page {");
        builder.AppendLine($"    size: {width}in {height}in;");
        builder.AppendLine("    margin: 0;");
        builder.AppendLine("}");
        builder.AppendLine("html, body {");
        builder.AppendLine("    margin: 0;");
        builder.AppendLine("    padding: 0;");
        builder.AppendLine($"    width: {width}in;");
        builder.AppendLine($"    height: {height}in;");
        builder.AppendLine("    overflow: hidden;");
        builder.AppendLine("    background: transparent;");
        builder.AppendLine("}");
        builder.AppendLine("svg {");
        builder.AppendLine("    display: block;");
        builder.AppendLine("    width: 100%;");
        builder.AppendLine("    height: 100%;");
        builder.AppendLine("}");
        builder.AppendLine("</style>");
        builder.AppendLine("</head>");
        builder.AppendLine("<body>");
        builder.Append(svg);
        builder.AppendLine();
        builder.AppendLine("</body>");
        builder.AppendLine("</html>");

        return builder.ToString();
    }
}
