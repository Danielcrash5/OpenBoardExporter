# OpenBoardExporter

Eigenständiger Windows-Exporter für OpenBoard `.ubx`-Archive.

OpenBoard selbst ist zur Laufzeit **nicht erforderlich**.

## Voraussetzungen

- Windows 10/11
- .NET 8 SDK
- Microsoft Edge WebView2 Runtime
- VS Code
- C# Dev Kit (empfohlen)

Der Exporter verwendet:
- `Microsoft.Web.WebView2` zum Rendern der aufbereiteten OpenBoard-SVG-Seiten und zum Erzeugen der PDFs.
- `Pdfium.Net.Free` zum Rendern eingebetteter PDF-Objekte und zum Zusammenführen der einzelnen Seiten.

## Projekt öffnen

```powershell
cd OpenBoardExporter
code .
```

Dann:

```powershell
dotnet restore
dotnet build -c Release
```

## Test

Eine einzelne UBX:

```powershell
dotnet run -c Release -- "C:\Backup\ITG 12.ubx"
```

Zwei UBX-Dateien:

```powershell
dotnet run -c Release -- `
  "C:\Backup\ITG 11.ubx" `
  "C:\Backup\ITG 12.ubx" `
  -o "C:\OpenBoard-PDF"
```

Kompletter Ordner:

```powershell
dotnet run -c Release -- `
  "C:\OpenBoard-Backup" `
  -o "C:\OpenBoard-PDF"
```

## Ausgabe

Die `dc:type`-Angabe aus `metadata.rdf` wird verwendet.

Beispiel:

```text
MyDocuments/ITG 12/Wirtschaft/Einstieg
```

wird zu:

```text
OpenBoard-PDF/
└── ITG 12/
    └── Wirtschaft/
        └── Einstieg/
            └── Dokumentname.pdf
```

## Warum der Exporter nicht nur die Stiftstriche nimmt

Eine OpenBoard-Seite besteht aus mehreren Ebenen:

- SVG-Hintergrund
- OpenBoard-Rich-Text (`foreignObject` + `itemTextContent`)
- Stift-/Vektorobjekte
- verschachtelte `images/*.svg`
- eingebettete `objects/*.pdf`
- OpenBoard-Hintergrundparameter wie `crossed-background`, `ruled-background` und `grid-size`

Der Exporter bereitet diese Elemente vor, bevor WebView2 die Seite als PDF rendert.

## Hinweis zu PDF-Objekten

OpenBoard referenziert eingebettete PDFs beispielsweise so:

```text
objects/{GUID}.pdf#page=2
```

Diese Referenzen werden nicht ignoriert. Die jeweilige PDF-Seite wird mit PDFium gerendert und an derselben Position in die OpenBoard-Seite eingesetzt.

## Hinweis

`Pdfium.Net.Free 3.0.1.6` ist laut NuGet inzwischen als deprecated markiert. Es wird hier verwendet, weil es die benötigte kostenlose PDFium-Funktionalität inklusive Rendering und PDF-Seitenimport in einem Paket bereitstellt. Für eine langfristige Version des Exporters sollte die PDFium-Schicht gegen eine aktuell gepflegte PDFium-Bindung ausgetauscht werden.

