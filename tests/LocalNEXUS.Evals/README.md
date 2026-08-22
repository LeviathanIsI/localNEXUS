# Eval harness

The test suite says the plumbing works. This says whether the output is any good.

Tests are pass or fail. Evals are numbers that move: the same fixed tasks, run repeatedly, so a
change to a prompt, a budget or a model can be judged against what came before it.

```
dotnet run --project tests/LocalNEXUS.Evals -c Release -- --models qwen --repeats 3
```

`--help` lists everything it accepts. It is slow, it needs a model on disk, it downloads nothing,
and it is not part of any build.

## What it measures

Per task, per repeat:

| | |
|---|---|
| First pass compile rate | files that compiled with no repair |
| Repair attempts, and whether it compiled in the end | read off the file the check emitted |
| Duplicate creation | whether the planner went for a second copy of a type the project already had |
| Reuse | whether it changed the existing type when that was the right answer |
| Plan completion | how much of the plan actually landed |
| Guardrail firing | which rule refused which file, and whether it was the rule the task was built to trip |
| Tokens, cost, wall time | plus model time and time to first token separately |

And, because they were cheap and each one has been a real failure at some point: files nobody asked
for, files that disappeared, scripts whose `.cs.meta` sibling went missing, markdown fences left in
generated code, replies cut off by the token ceiling, and how much code came out.

Everything is either counted directly or read off disk afterwards. Nothing reads the model's prose
to decide whether it did well, because a harness cannot know that, and a number derived that way
would be confidently wrong while looking like a quality score.

Nothing here reads the activity feed. It used to: repair attempts were counted by matching the
wording of a log entry, and refusals were counted from the staging list, which could say that a
write had been refused but not which of seven rules had refused it. Both were reading prose. The
application now records the repair count on the file it emits and files every refusal and every
planning verdict on the run as structured decisions, so the harness reads values.

Two measurements are worth understanding before trusting them:

- **Duplicate creation is measured at the point it is decided, not where it would land.** Measuring
  it on disk gave zero every time, because the write guard stops a duplicate reaching disk. A
  prevented attempt is still the planner being wrong and the guard covering for it, which is worth
  knowing separately from the planner being right.
- **Reuse by reference is not measured.** A plan that writes a new file which calls an existing type
  is doing the right thing, and nothing links a plan row to the candidate verdict that named the
  symbol. Inventing that link would be a guess, so the verdicts are reported raw instead.

## The task set

Six shapes, because each fails differently:

| Task | What it exercises |
|---|---|
| `new-file-alone` | close to a floor: a model that cannot do this cannot do any of the rest |
| `new-file-references-existing` | the index and the ranking; without them a model invents a plausible wrong name |
| `edit-existing` | whether the model can be trusted with something already working |
| `multi-file-ordered` | dependency ordering and the accumulated compile |
| `should-edit-not-create` | the duplicate guard, which is the one failure this application exists to prevent |
| `unity-refusal` | a change that compiles cleanly and silently breaks a scene |

Each starts from the same small generated project, under the system temp folder, deleted after.
Never the repository, never a real project, never anywhere the application keeps its own data.

**The set is versioned and the version is written into every result.** Changing anything about a
task, including its wording, means a new version, because results across versions are not
comparable and nothing would say so otherwise. The seed project is deliberately small: a large one
would make the ranking the dominant variable, and this measures the whole pipeline.

## Results

Written to an `evals/` folder in the repository by default, overridable with `--out`. Three shapes:

- `<timestamp>-<model>.md` — the summary, meant to be read
- `<timestamp>-<model>.json` — everything, for anything the summary does not answer
- `history.csv` — one row per task appended across every run, so a series reads as a series

Deliberately not `%LOCALAPPDATA%\LocalNEXUS\`. Results are the one thing here whose value is
accumulating across weeks, and keeping them among the models, the config and the credentials meant
anything clearing application data took the record with it. That happened three times in one
session and `history.csv` went with it every time.

Every one carries the conditions that produced it: model, quantization, context size, GPU layers,
temperature, token ceiling, planner budget, task set version, app version and machine. A number
without its conditions cannot be compared with anything, and comparison is the entire point.

## What this is not

**Not a gate.** Nothing here fails on a threshold. A model is not deterministic and a task that
comes out right four times in five is ordinary, so a build that went red on the fifth would teach
nobody anything.

**Not in CI.** It is slow and needs a model present.

**Not something to tune against.** The harness measures. Changing the application to move a number
is a separate decision, made by a person who has looked at why the number is what it is.
