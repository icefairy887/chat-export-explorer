# Local embedding model

Lumina expects the following runtime files when running from source:

```text
Models/all-MiniLM-L6-v2/model.onnx
Models/all-MiniLM-L6-v2/vocab.txt
```

The model binaries are intentionally excluded from Git. Copy the locally
verified model files into that directory before running or publishing Lumina.
Release packages include the model beside the application.
