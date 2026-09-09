#!/usr/bin/env python3
"""Reproducible retrieval comparison. Synthetic fixtures never prove personal quality."""
from __future__ import annotations

import argparse
import gc
import hashlib
import importlib.metadata
import json
import math
from pathlib import Path
import platform
import re
import sys
import time

import numpy as np

from message_embeddings import encode_texts, get_encoder

DEFAULT_CASES = Path(__file__).parent / "tests/fixtures/retrieval.jsonl"
DEFAULT_MODELS = ["all-MiniLM-L6-v2", "BAAI/bge-m3", "FacebookAI/roberta-base"]


def load_cases(path):
    items = [json.loads(line) for line in Path(path).read_text(encoding="utf-8").splitlines() if line.strip()]
    documents = [r for r in items if r["type"] == "document"]
    queries = [r for r in items if r["type"] == "query"]
    ids = [r["id"] for r in documents]
    if not documents or not queries or len(set(ids)) != len(ids):
        raise ValueError("Need documents with unique IDs and at least one query")
    if len({q['id'] for q in queries}) != len(queries):
        raise ValueError("Query IDs must be unique")
    for query in queries:
        if not query["relevant"] or not set(query["relevant"]) <= set(ids):
            raise ValueError(f"Invalid relevance IDs: {query['id']}")
    return documents, queries


def score_rankings(scores, documents, queries, k):
    details = []
    for query, similarities in zip(queries, scores):
        order = sorted(range(len(documents)), key=lambda i: (-float(similarities[i]), documents[i]["id"]))
        ranked = [documents[i]["id"] for i in order[:k]]
        relevant = set(query["relevant"])
        hits = [i + 1 for i, doc_id in enumerate(ranked) if doc_id in relevant]
        dcg = sum(1 / math.log2(rank + 1) for rank in hits)
        ideal = sum(1 / math.log2(rank + 1) for rank in range(1, min(len(relevant), k) + 1))
        details.append(dict(query_id=query["id"], category=query.get("category", "unspecified"),
                            hit_at_1=float(ranked[0] in relevant),
                            recall_at_k=len(hits) / len(relevant),
                            mrr_at_k=1 / hits[0] if hits else 0,
                            ndcg_at_k=dcg / ideal,
                            expected=sorted(relevant), ranked_ids=ranked))
    metrics = {name: float(np.mean([row[name] for row in details]))
               for name in ("hit_at_1", "recall_at_k", "mrr_at_k", "ndcg_at_k")}
    return dict(metrics=metrics, queries=details)


def lexical_scores(documents, queries):
    tokenize = lambda text: set(re.findall(r"\w+", text.lower()))
    docs = [tokenize(d["text"]) for d in documents]
    return np.array([[len(tokenize(q["text"]) & d) / max(1, len(tokenize(q["text"]) | d))
                      for d in docs] for q in queries])


def evaluate_model(name, documents, queries, batch_size):
    get_encoder.cache_clear()
    gc.collect()
    start = time.perf_counter()
    encoder = get_encoder(name)
    load_seconds = time.perf_counter() - start
    start = time.perf_counter()
    docs = encode_texts(encoder, [d["text"] for d in documents], batch_size=batch_size)
    document_seconds = time.perf_counter() - start
    start = time.perf_counter()
    query_vectors = encode_texts(encoder, [q["text"] for q in queries], query=True, batch_size=batch_size)
    query_seconds = time.perf_counter() - start
    docs /= np.linalg.norm(docs, axis=1, keepdims=True)
    query_vectors /= np.linalg.norm(query_vectors, axis=1, keepdims=True)
    config = getattr(getattr(encoder[0], "auto_model", None), "config", None)
    metadata = dict(model=name, resolved_revision=getattr(config, "_commit_hash", None),
                    dimensions=docs.shape[1], max_seq_length=encoder.max_seq_length,
                    query_prompt=encoder.prompts.get("query"), device=str(encoder.device),
                    load_seconds=load_seconds, document_seconds=document_seconds,
                    query_seconds=query_seconds,
                    experiment="untrained mean pooling" if name == "FacebookAI/roberta-base" else "retrieval model")
    return query_vectors @ docs.T, metadata


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--cases", type=Path, default=DEFAULT_CASES)
    parser.add_argument("--models", nargs="+", default=DEFAULT_MODELS,
                        help="Use lexical for an offline fixture check; Qwen/Qwen3-Embedding-4B is also supported")
    parser.add_argument("--output", type=Path, default=Path("artifacts/embedding-evaluation.json"))
    parser.add_argument("--batch-size", type=int, default=8)
    parser.add_argument("--k", type=int, default=5)
    args = parser.parse_args()
    if args.k < 1 or args.batch_size < 1:
        parser.error("--k and --batch-size must be positive")
    documents, queries = load_cases(args.cases)
    versions = {}
    for package in ("numpy", "sentence-transformers", "transformers", "torch"):
        try:
            versions[package] = importlib.metadata.version(package)
        except importlib.metadata.PackageNotFoundError:
            versions[package] = "not installed"
    report = dict(dataset=str(args.cases), dataset_sha256=hashlib.sha256(args.cases.read_bytes()).hexdigest(),
                  limitation="Bundled cases are synthetic. No private archive was evaluated. Custom cases require their own provenance review.",
                  documents=len(documents), queries=len(queries), k=args.k,
                  python=platform.python_version(), versions=versions, results=[])
    args.output.parent.mkdir(parents=True, exist_ok=True)
    failed = False
    for name in args.models:
        print(f"Evaluating {name}...", flush=True)
        try:
            if name == "lexical":
                scores, metadata = lexical_scores(documents, queries), dict(model=name)
            else:
                scores, metadata = evaluate_model(name, documents, queries, args.batch_size)
            result = dict(status="ok", **metadata, **score_rankings(scores, documents, queries, args.k))
            print(json.dumps(result["metrics"]), flush=True)
        except Exception as exc:
            result = dict(model=name, status="failed", error=f"{type(exc).__name__}: {exc}")
            failed = True
            print(result["error"], file=sys.stderr, flush=True)
        report["results"].append(result)
        args.output.write_text(json.dumps(report, indent=2, ensure_ascii=True) + "\n", encoding="utf-8")
    print(f"Report: {args.output}")
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
