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

### 1. Twenty tasks

Task set v3. The original six are untouched, request text and seed project included, so a per task
comparison against a v2 result still holds for them. A set total does not compare, because the
denominator went from six to twenty and the fourteen added are deliberately harder.

The fourteen: interface-two-implementations, scriptable-object, change-nothing, ambiguous-request,
edit-two-files, extend-not-sibling, rename-bound-class, serialized-rename-with-shim, oversized-plan,
missing-type-must-create, routine-enum, routine-utility, nested-folder, namespace-move-refused.

The last two are the ones chosen freely. Every file the set had ever written landed in one flat
folder, so `nested-folder` is the first to ask for a directory that does not exist. And the set
tripped two of the seven write rules ever, so `namespace-move-refused` adds a third.

Three new outcome kinds needed scoring that did not exist: a task whose right answer is to write
nothing, a task whose right answer is to ask, and a refusal that could correctly come from either
of two rules. `EvalTask` gained `ExpectsNoChange`, `ExpectsClarification` and
`AcceptableRefusalRules` in place of the single expected rule.

One production change, for measurability rather than for a number. Triage asks a clarifying
question by writing to the activity feed and nowhere else, so nothing could count how often a run
guessed where it should have asked. `RunDecisionKind.ClarificationAsked` is recorded at the point
the question is asked, which is the same pattern v1.24 established. No decision logic changed.

A three task smoke of the new shapes already turned up two things worth the morning:
`ambiguous-request` planned five files for "Make it faster." without asking anything, and
`routine-enum` planned five files for a three value enum.
