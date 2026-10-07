using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenBoardExporter;

public sealed class CommandLineOptions
{
    public List<string> InputFiles { get; } = [];
    public string OutputDirectory { get; private set; } =
        Path.Combine(Environment.CurrentDirectory, "OpenBoard-PDF");

    public bool ShowHelp { get; private set; }

    public static string Help => """
OpenBoardExporter

Verwendung:
  OpenBoardExporter.exe <datei.ubx> [datei2.ubx ...] [-o <ausgabeordner>]
  OpenBoardExporter.exe <ordner> [-o <ausgabeordner>]

Beispiele:
  OpenBoardExporter.exe "C:\Backup\ITG 11.ubx"
  OpenBoardExporter.exe "C:\Backup\ITG 11.ubx" "C:\Backup\ITG 12.ubx" -o "C:\OpenBoard-PDF"
  OpenBoardExporter.exe "C:\Backup" -o "C:\OpenBoard-PDF"

Wenn als Eingabe ein Ordner angegeben wird, werden alle .ubx-Dateien
rekursiv verarbeitet.
""";

    public static CommandLineOptions Parse(string[] args)
    {
        var result = new CommandLineOptions();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            if (arg is "-h" or "--help" or "/?")
            {
                result.ShowHelp = true;
                return result;
            }

            if (arg is "-o" or "--output")
            {
                if (++i >= args.Length)
                    throw new ArgumentException("Nach -o/--output fehlt der Ausgabeordner.");

                result.OutputDirectory = Path.GetFullPath(args[i]);
                continue;
            }

            var full = Path.GetFullPath(arg);

            if (Directory.Exists(full))
            {
                result.InputFiles.AddRange(
                    Directory.EnumerateFiles(full, "*.ubx", SearchOption.AllDirectories)
                        .OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
            }
            else if (File.Exists(full) && string.Equals(Path.GetExtension(full), ".ubx", StringComparison.OrdinalIgnoreCase))
            {
                result.InputFiles.Add(full);
            }
            else
            {
                throw new FileNotFoundException($"Eingabe nicht gefunden oder keine .ubx-Datei: {arg}");
            }
        }

        return result;
    }
}
