# Embedding comparison — 2026-09-09

Actual CPU inference on 30 synthetic messages and 14 labeled queries. These are deliberately small diagnostic cases, not a held-out benchmark or private-archive evaluation. No model was tuned on these cases.

| Model | Correct first result | Recall@5 | MRR@5 | nDCG@5 | Dimensions | Encode docs (s) | Encode queries (s) |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| lexical | 5/14 | 0.607 | 0.488 | 0.514 | — | 0.00 | 0.00 |
| all-MiniLM-L6-v2 | 8/14 | 0.964 | 0.732 | 0.786 | 384 | 0.10 | 0.03 |
| BAAI/bge-m3 | 11/14 | 0.964 | 0.875 | 0.889 | 1024 | 2.46 | 0.63 |
| FacebookAI/roberta-base | 5/14 | 0.714 | 0.506 | 0.558 | 768 | 0.50 | 0.17 |
| Qwen/Qwen3-Embedding-4B | 12/14 | 1.000 | 0.929 | 0.937 | 2560 | 42.89 | 30.52 |

Qwen3-Embedding-4B leads this fixture. BGE-M3 is a smaller comparison target and the default demo model. Raw RoBERTa with mean pooling did not improve the baseline. These observations justify further testing; they do not establish a universally best model.

Timing excludes download/model load and is from one run per model. Qwen used batch size 1 and its configured weight precision; other models used batch size 8. Timing is therefore not a controlled hardware benchmark. Ranking used model-defined query/document encoding and normalized cosine similarity.

## Per-query first result

| Query | Expected | Lexical | MiniLM | BGE-M3 | RoBERTa | Qwen 4B |
| --- | --- | --- | --- | --- | --- | --- |
| q01 | d01 | d01 ✓ | d01 ✓ | d01 ✓ | d16 ✗ | d01 ✓ |
| q02 | d04, d23 | d08 ✗ | d08 ✗ | d08 ✗ | d08 ✗ | d04 ✓ |
| q03 | d06 | d14 ✗ | d05 ✗ | d06 ✓ | d17 ✗ | d06 ✓ |
| q04 | d07 | d24 ✗ | d07 ✓ | d07 ✓ | d09 ✗ | d07 ✓ |
| q05 | d09 | d09 ✓ | d09 ✓ | d09 ✓ | d09 ✓ | d09 ✓ |
| q06 | d12 | d13 ✗ | d13 ✗ | d07 ✗ | d12 ✓ | d12 ✓ |
| q07 | d14 | d14 ✓ | d14 ✓ | d14 ✓ | d14 ✓ | d14 ✓ |
| q08 | d16 | d17 ✗ | d05 ✗ | d18 ✗ | d30 ✗ | d16 ✓ |
| q09 | d18 | d14 ✗ | d18 ✓ | d18 ✓ | d17 ✗ | d18 ✓ |
| q10 | d20 | d20 ✓ | d21 ✗ | d20 ✓ | d21 ✗ | d20 ✓ |
| q11 | d21 | d20 ✗ | d21 ✓ | d21 ✓ | d21 ✓ | d20 ✗ |
| q12 | d22 | d30 ✗ | d30 ✗ | d22 ✓ | d30 ✗ | d30 ✗ |
| q13 | d27 | d27 ✓ | d27 ✓ | d27 ✓ | d29 ✗ | d27 ✓ |
| q14 | d25 | d10 ✗ | d25 ✓ | d25 ✓ | d25 ✓ | d25 ✓ |

Fixture definitions: [retrieval.jsonl](../../tests/fixtures/retrieval.jsonl). The roles/provenance cases test retrieval of statements about authorship; the separate regression tests enforce actual role filtering.

## Reproducibility

Dataset SHA-256: `f7c52a1b0c812140c40a38b99ffdbe515c5f6ffa260e0058dac416da5af801fb`

Python 3.12.14. Dependency versions:

- numpy: 2.3.5
- sentence-transformers: 5.7.0
- transformers: 4.57.6
- torch: 2.14.0+cpu

| Model | Resolved revision | Max input tokens |
| --- | --- | ---: |
| all-MiniLM-L6-v2 | `1110a243fdf4706b3f48f1d95db1a4f5529b4d41` | 256 |
| BAAI/bge-m3 | `5617a9f61b028005a4858fdac845db406aefb181` | 8192 |
| FacebookAI/roberta-base | `e2da8e2f811d1448a5b465c236feacd80ffbac7b` | 512 |
| Qwen/Qwen3-Embedding-4B | `5cf2132abc99cad020ac570b19d031efec650f2b` | 40960 |

Model revisions are recorded as loaded. Re-running by model name can retrieve a newer revision. The bundled short-message test does not measure long-message truncation, cross-conversation context, full-history scale, or C# detector quality.

Run instructions: [RUN-LUMINA.md](../RUN-LUMINA.md).

## Primary reference documentation

- [Qwen3-Embedding-4B model card](https://huggingface.co/Qwen/Qwen3-Embedding-4B): model-specific retrieval usage.
- [BGE-M3 model card](https://huggingface.co/BAAI/bge-m3): dense retrieval support; this experiment does not use its sparse or multi-vector outputs.
- [RoBERTa model card](https://huggingface.co/FacebookAI/roberta-base): pretrained masked-language model, generally intended for downstream fine-tuning.
- [Sentence Transformers retrieval usage](https://sbert.net/docs/sentence_transformer/usage/usage.html): distinct query/document prompts.
