# Local embedding model

Lumina expects the following runtime files when running from source:

```text
Models/all-MiniLM-L6-v2/model.onnx
Models/all-MiniLM-L6-v2/vocab.txt
```

The model binaries are intentionally excluded from Git. Run
`lumina-dotnet\setup.ps1` after cloning the repository. The script downloads
the compatible ONNX export and vocabulary, verifies pinned SHA-256 hashes, and
places them in this directory. Release packages include the verified model
beside the application.
