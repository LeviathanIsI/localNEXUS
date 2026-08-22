# Overnight eval expansion, working log

Started from `0cefe0a`, working tree clean. That commit is the rollback point.

This is the log kept while the work happened. The thing to read in the morning is
`docs/eval-night-report.md`; this file is here so the order of events is recoverable.

## Conditions found at the start

- Models on disk: `qwen2.5-coder-7b-instruct-q4_k_m.gguf` and nothing else.
- `qwen2.5-0.5b-instruct-q4_k_m` is **not present**. Section 3 asks for it and downloading is
  forbidden, so that section cannot be run as written. Recorded and worked around, see below.
- Task set at the start: v2, six tasks.

## Section log

### 0. Rollback point

Nothing to commit; the tree was already clean at `0cefe0a`. This log is the first change.
