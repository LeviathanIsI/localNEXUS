# Overnight eval expansion, working log

Started from `263790a`, working tree clean. That commit is the rollback point.

This is the log kept while the work happened. The thing to read in the morning is
`docs/eval-night-report.md`; this file is here so the order of events is recoverable.

## Conditions found at the start

- Models on disk: `qwen2.5-coder-7b-instruct-q4_k_m.gguf` and nothing else.
- `qwen2.5-0.5b-instruct-q4_k_m` is **not present**. Section 3 asks for it and downloading is
  forbidden, so that section cannot be run as written. Recorded and worked around, see below.
- Task set at the start: v2, six tasks.

## Section log

### 0. Rollback point

Nothing to commit; the tree was already clean at `263790a`. This log is the first change.

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

### 3. Model comparison, not done

`qwen2.5-0.5b-instruct-q4_k_m` is not on this machine. The models folder holds exactly one GGUF,
the 7B coder, and downloading is forbidden, so the comparison cannot be run.

There is an Ollama store at `~/.ollama` which may hold other weights. Reading it, or copying
anything out of it into the models folder, is outside what tonight's rules allow: nothing outside
the repository except the eval's own temp folders, and nothing in the LocalNEXUS data folder beyond
reading the models directory. So it was left alone. If a 0.5B is wanted for this comparison the
cheapest route in the morning is to put one GGUF in
`%LOCALAPPDATA%\LocalNEXUS\models\gguf` and run:

    dotnet run --project tests/LocalNEXUS.Evals -c Release -- --repeats 1

The harness runs every model it finds, so both appear in the same results folder and `history.csv`
carries the model on every row, which is what makes the side by side possible without any further
work.

What the section wanted to establish, a documented floor for what the application does when the
model is not capable enough, is therefore still unknown.

### 2. Ten stability runs

Two hundred task runs, 157 passed. Ran clean end to end. The analysis had to be redone once,
because `history.csv` had a stale header and every column after the first added one was shifted by
two while still parsing. That is written up as D9 in the report and is the one thing fixed
tonight, on the grounds that reporting numbers known to be wrong is worse than a small change to
measurement code.

### 4. Debate and Judge

First attempt faulted before the debate started: a model node handed to something on its Model pin
is still executed as a node, and throws when its own Text pin is empty. That is D2 and it means
neither node has ever been runnable as designed. Worked around in the probe by wiring the subject
into both models, which costs two completions nobody reads, and the workaround is stated in the
transcript itself so the file does not mislead anybody reading it alone.

Second attempt ran all six rounds, never settled, and fell to the judge. The convergence numbers
are the most useful thing the night produced and they are bad: the whole six round score rests on
one shared token. Written up as D5 with the per round breakdown.

### 5. Report

`docs/eval-night-report.md`, with the full transcript as an appendix so the whole thing is one
file as asked.

## What was fixed and what was not

Fixed: the history file header drift, because it made the night's data unreadable.

Not fixed, deliberately: everything else. Ten defects, all in the report with enough detail to act
on, in a suggested order.

## Afterwards: D2 fixed in v1.28

The reference only model node is fixed and the workaround is out of the probe. A debate now runs
end to end from a graph wired the way the Model pin was introduced for. The transcript in the
appendix of the report is the one taken with the workaround in place; the run without it settled
after one round at 80 percent, which is sampling rather than anything the fix changed.

Everything else in the report stands unfixed.

**Where these results live:** an `evals/` folder in the repository, gitignored. It moved there in
`255d110`; it used to sit inside the application's data folder, which meant clearing app data took
the record with it, and that happened three times during this session.
