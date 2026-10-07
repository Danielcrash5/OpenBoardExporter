using System.Windows;

namespace OpenBoardExporter;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var app = new System.Windows.Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };

        var window = new MainWindow(args);
        app.Run(window);
    }
}
