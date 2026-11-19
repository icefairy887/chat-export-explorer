# Run Lumina and test the search changes

Local Python checks and real model comparisons passed. The Windows GUI and scripts still need a Windows run; the C# build attempt here was blocked during package restore.

Start with the synthetic demo. It lets you compare Keywords and Meaning without importing your full archive. These changes are on branch `codex/lumina-search-evaluation`; the existing Windows release does not include them yet.

## 1. Get this version in VS Code

Open a PowerShell terminal in your existing `chat-export-explorer` folder. For the previously cloned location:

```powershell
cd "$HOME\chat-export-explorer"
git status --short
git fetch origin
git switch codex/lumina-search-evaluation
git pull --ff-only
code .
```

If Git reports local changes and refuses to switch, keep those changes and stop at that error. Do not use `reset --hard`. If your checkout lives somewhere else, open that folder first.

## 2. Run the checks and model comparison

Use Python 3.12 with the Windows `py` launcher. In VS Code's terminal:

```powershell
.\scripts\Test-Search.ps1
```

The script creates `.venv`, installs the semantic dependencies, runs regression checks, and compares:

- lexical word overlap (an offline baseline, not the app's FTS ranking)
- the existing MiniLM model
- BGE-M3
- raw RoBERTa with mean pooling, as an experimental control

The first run downloads model files. BGE-M3 is a substantial download. Later runs reuse the model cache. No API key is needed. You should see `OK` after the regression checks and a metrics line for each model.

Open the results:

```powershell
code .\artifacts\embedding-evaluation.json
```

`hit_at_1` means the fraction of questions with a correct first result. `recall_at_k` measures relevant answers found in the first five. `mrr_at_k` rewards placing the first correct answer higher. `ndcg_at_k` also accounts for questions with multiple correct answers. Higher is better. These 14 synthetic queries are a smoke benchmark, not a final quality gate for personal analysis.

To test the larger Qwen candidate separately:

```powershell
.\scripts\Test-Search.ps1 -Models 'Qwen/Qwen3-Embedding-4B' -BatchSize 1
```

This uses the model's declared weight precision and query prompt. It requires substantially more memory and disk space. Each script run replaces the local results JSON; save a copy if you want to retain both runs. Download/load failures are recorded as failures and return a nonzero exit code.

The recorded comparison from this review is in [EMBEDDING-RESULTS.md](review/EMBEDDING-RESULTS.md): Qwen 4B scored 12/14 correct first results, BGE-M3 11/14, MiniLM 8/14, and raw RoBERTa 5/14.

## 3. Open the search demo

```powershell
.\scripts\Start-SearchDemo.ps1
```

When the terminal prints the address, open:

<http://127.0.0.1:5000/search>

Select **Meaning**, keep the speaker set to **You** or **Everyone**, and try:

| Query | What to inspect |
| --- | --- |
| `A company wants to talk to me about a possible job.` | An invitation to speak with a hiring manager |
| `Search is matching vocabulary instead of intent.` | A message about matching words while missing meaning |
| `The encoder changed but the index was never regenerated.` | Old vectors left behind after a model change |
| `Jordan backed out while Taylor still planned to come.` | Correct person/action pairing; compare the reversed example |
| `The AI said it, so it is not evidence that I experienced it.` | The user's distinction between assistant text and personal evidence |

Switch the same query to **Keywords** and compare. Click a result to open its source message. Similarity is a ranking score, not a probability that a claim is true.

The demo creates `artifacts\search-demo.db` and a matching `search-demo-export.json`. The script also builds daily trajectory states so the dashboard, Browse, Patterns, Trajectory, and Simulate pages can be explored. The 30 independent examples do not represent a person's actual history; timeline and simulation outputs are only interface demonstrations.

Press **Ctrl+C** in the terminal to stop the server. To use MiniLM with the same demo:

```powershell
.\scripts\Start-SearchDemo.ps1 -Model 'all-MiniLM-L6-v2'
```

Each model gets its own message index. Daily trajectory calculations still use their existing MiniLM implementation.

To try the strongest model from this small comparison in the demo:

```powershell
.\scripts\Start-SearchDemo.ps1 -Model 'Qwen/Qwen3-Embedding-4B' -BatchSize 1
```

## 4. Try your own archive

Stop the demo server first. Use a new database filename: the existing import tool replaces its destination database if it already exists.

```powershell
$python = '.\.venv\Scripts\python.exe'
$env:CHAT_DB = Join-Path $PWD 'artifacts\my-search-trial.db'
$env:CHAT_EMBEDDING_MODEL = 'BAAI/bge-m3'
& $python import_export.py 'C:\path\to\conversations.json' --database $env:CHAT_DB
& $python lumina_doctor.py
& $python semantic_search.py --model $env:CHAT_EMBEDDING_MODEL --build-only --batch-size 8
& $python build_trajectories.py
& $python archive_server.py --port 5000
```

Replace only the export path with your actual file. The database check prints counts and model names, not chat text. Inspect each command's output before continuing. On a large archive, index creation takes much longer than the demo. Use `--batch-size 1` if memory is tight.

You can also search directly in the terminal:

```powershell
& $python semantic_search.py --model 'BAAI/bge-m3' --role user --limit 10 'the thing you want to find'
```

Use `--db` to explicitly select a database. Otherwise the command follows `CHAT_DB`, then the same app-data default as Archive Explorer. On Windows the default is `%LOCALAPPDATA%\ChatExportExplorer\chat_history.db`.

## 5. Open the C# Lumina desktop interface

This is the second application in the same repository. It requires Windows and the .NET 10 SDK for a source run. Its embedding engine remains the existing MiniLM ONNX implementation; the Python model comparison does not change it.

From the repository root, in a separate terminal:

```powershell
cd .\lumina-dotnet
.\setup.ps1
dotnet run --project .\ChatAnalyzer.EmbedTest\ChatAnalyzer.EmbedTest.csproj
dotnet run --project .\ChatAnalyzer.Desktop\ChatAnalyzer.Desktop.csproj
```

The first script downloads and verifies the existing ONNX model. The embedding check prints dimensions and related/unrelated similarities. The current check reports values rather than enforcing a pass/fail threshold. In Lumina, choose **Try a demo**, then **Analyze my timeline**, then inspect **Insights**, **Show the receipts**, and **Signal timeline**. Leave Advanced AI settings on **Local analysis only** for this run.

For a terminal analysis of the generated export:

```powershell
dotnet run --project .\ChatAnalyzer.App\ChatAnalyzer.App.csproj -- ..\artifacts\search-demo-export.json
```

The console app creates `lumina-dotnet\analyzer.db`. The desktop app uses `%LOCALAPPDATA%\Lumina\analyzer.db`. They are separate from the Python archive database. The synthetic search export can produce few or no longitudinal findings; use Lumina's built-in demo to explore its timeline presentation.

**Open Archive Explorer** inside the C# app requires the bundled server executable. A plain source run does not automatically build that executable. To build the full existing suite:

```powershell
.\build-windows.ps1
```

Its ZIP appears under `lumina-dotnet\artifacts`. That packaged archive server remains an archive-only build: the Python Meaning feature requires the source environment above, with its ML dependencies. The package does not yet ship BGE-M3 or a semantic Python runtime.

## 6. Return to your normal setup

After stopping the source server:

```powershell
Remove-Item Env:CHAT_DB -ErrorAction SilentlyContinue
Remove-Item Env:CHAT_EMBEDDING_MODEL -ErrorAction SilentlyContinue
```

To return to the main code branch, from the repository root:

```powershell
git switch main
```

The demo database and model downloads remain local. No Azure resources are created by these steps. Azure OpenAI adapters exist in the desktop source; Azure hosting and Azure SQL are separate future work.
