# Lumina

Lumina is a local-first Windows application for longitudinal analysis of
ChatGPT exports. It imports one or more `conversations.json` files, merges
overlapping exports, builds a local semantic timeline, extracts persistent
user-authored evidence events, and identifies changes across time.

The full archive, embeddings, and SQLite database remain on the computer.
Lumina stores working data in `%LOCALAPPDATA%\Lumina\analyzer.db`.

## Easiest install: GitHub release

1. Open the repository's **Releases** page.
2. Download the newest `Lumina-Suite-*-win-x64.zip` file.
3. Extract the ZIP completely.
4. Double-click `Lumina.exe`. Do not launch it through `dotnet run` or a
   command window.
5. Drop one or more ChatGPT conversation JSON exports anywhere in Lumina, use
   **Choose export files**, select **Find exports**, or start with **Try a
   demo**.
6. Select **Analyze my timeline**.
7. Review **Insights**, then open **Signal timeline** to inspect dated events.

The release is self-contained. It does not require a separate .NET install and
does not include private conversation data.

## Run from source

```powershell
./setup.ps1
dotnet run --project .\ChatAnalyzer.Desktop\ChatAnalyzer.Desktop.csproj
```

`setup.ps1` downloads the compatible MiniLM ONNX model from Hugging Face and
verifies both model files with pinned SHA-256 hashes before installation.

In the app:

1. Leave **Advanced AI settings** collapsed for fully local analysis.
2. Drop or choose one or more ChatGPT `conversations.json` export files.
3. Select **Analyze my timeline**.
4. Review the dashboard counters and evidence-backed finding cards.
5. Open **Show the receipts** under a finding to inspect its source.
6. Open **Signal timeline** to inspect chronological user-authored events.

## Publish Windows x64

```powershell
./build-windows.ps1
```

The script downloads/verifies the model, publishes the self-contained Windows
app, includes a locally built Archive Explorer when available, and writes a ZIP
under `artifacts\`.

## Product boundary

The bundled Chat Export Explorer remains the archive/search interface. Lumina
is the longitudinal analysis interface. The **Open Archive Explorer** button
opens the archive inside a native Lumina WebView window. A bundled
`Archive Explorer\Chat Export Explorer Server.exe` serves the existing search
interface only on `127.0.0.1`; it does not use pywebview or Python.NET.

Private exports, SQLite databases, and generated analysis data must not be
committed to source control.
