using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace OpenBoardExporter;

public readonly record struct SvgPageSize(
    double WidthPixels,
    double HeightPixels)
{
    public double WidthInches => WidthPixels / 96.0;
    public double HeightInches => HeightPixels / 96.0;

    public static SvgPageSize Parse(string svg)
    {
        var match = Regex.Match(
            svg,
            @"<svg\b[^>]*\bviewBox\s*=\s*[""']\s*([-+0-9.eE]+)\s+([-+0-9.eE]+)\s+([-+0-9.eE]+)\s+([-+0-9.eE]+)\s*[""']",
            RegexOptions.IgnoreCase);

        if (!match.Success)
            throw new InvalidDataException("SVG besitzt kein gültiges viewBox.");

        var width = double.Parse(match.Groups[3].Value, CultureInfo.InvariantCulture);
        var height = double.Parse(match.Groups[4].Value, CultureInfo.InvariantCulture);

        if (width <= 0 || height <= 0)
            throw new InvalidDataException("Ungültige SVG-Seitengröße.");

        return new SvgPageSize(width, height);
    }
}
