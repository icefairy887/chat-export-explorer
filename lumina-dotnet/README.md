# Lumina

Lumina is a local-first Windows application for longitudinal analysis of
ChatGPT exports. It imports one or more `conversations.json` files, merges
overlapping exports, builds a local semantic timeline, extracts persistent
user-authored evidence events, and identifies changes across time.

The full archive, embeddings, and SQLite database remain on the computer.
Lumina stores working data in `%LOCALAPPDATA%\Lumina\analyzer.db`.

## Run from source

```powershell
dotnet run --project .\ChatAnalyzer.Desktop\ChatAnalyzer.Desktop.csproj
```

In the app:

1. Leave **LLM Provider** set to **Local analysis only**.
2. Select **Add ChatGPT Exports**.
3. Choose one or more ChatGPT `conversations.json` export files.
4. Select **ANALYZE**.
5. Expand **Evidence & receipts** under a finding to inspect its source.

## Publish Windows x64

```powershell
dotnet publish .\ChatAnalyzer.Desktop\ChatAnalyzer.Desktop.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\Lumina
```

The local MiniLM model is copied into the published output automatically.
Before building from a fresh source checkout, place `model.onnx` and
`vocab.txt` under `Models\all-MiniLM-L6-v2`; see `Models\README.md`.

## Product boundary

The bundled Chat Export Explorer remains the archive/search interface. Lumina
is the longitudinal analysis interface. The **Open Archive Explorer** button
launches a bundled copy from `Archive Explorer\Chat Export Explorer.exe`.

Private exports, SQLite databases, and generated analysis data must not be
committed to source control.
