# Security

## Reporting

Do not open a public issue.

[Open a security advisory](https://github.com/You-Know-Its-Me-Studios/LocalNEXUS/security/advisories/new).
That gives a private thread and, if it is real, a way to publish an advisory and credit you.
If advisories are not available to you, email joshua.r.bradford1@gmail.com with "LocalNEXUS
security" in the subject.

Include enough to reproduce it: the version, what an attacker has to control, and what they
get. Proof of concept code is welcome.

One maintainer, so set expectations accordingly. Acknowledgement within a week. Valid reports
ship a fix in the next release with the advisory. Disagree with an assessment and say so, it
will not be held against you.

Only the latest release is supported. Pre-1.0, no maintenance branches.

## What this app does, before you decide what counts as a vulnerability

None of the following is a bug. It is what the tool is.

**It runs code a language model wrote.** Files land in your project and Unity compiles and
runs them. The compile check verifies that code builds, not that it is safe. Review what gets
written, especially if the model came from somewhere you do not control.

**A Patch node executes arbitrary C#.** In script mode it compiles an expression from the
graph with Roslyn and runs it in this process with this process's privileges. Roslyn scripting
is not a sandbox and was never meant to be one.

**So a graph file is executable content, not data.** Opening a `.nexusgraph.json` someone sent
you is running a program they sent you. There is no warning in the interface about this, which
is itself worth fixing.

**API keys are plain text.** In `config.json` and inside any saved graph with a hosted Model
node. Share or commit a graph and the key goes with it. Strip the `apiKey` fields first.

**It starts child processes and opens ports.** `llama-server` binds `127.0.0.1` on a per model
port, so it is not reachable from the network. It runs with permissive CORS and no API key,
which is only safe because of that binding. The mesh node listens on a configurable port,
9337 by default, LAN scoped unless you publish.

**Engine processes are owned through Windows job objects**, so the OS kills them when the
app's handle closes. The app never terminates a process it did not start.

**It writes to your filesystem.** The Output node resolves paths through the project service,
which refuses anything landing outside the opened project. Escaping that is a vulnerability.

## In scope

- Escaping the project folder when writing files.
- Reaching the local inference server or mesh node from outside the machine when it should not
  be reachable.
- Anything making it more dangerous to open a graph, project or model file than described
  above.
- Leaking API keys beyond the plain text storage described above.
- Privilege escalation, or an engine process outliving the app in a way the job object was
  supposed to prevent.

## Not in scope

- A model producing bad or malicious code. That is what the compile check and your review are
  for.
- Anything requiring an attacker to already have code execution on your machine.
- Vulnerabilities in llama.cpp, Mesh LLM, uv, torch or transformers. Report upstream. Tell us
  anyway if we ship an affected version.
- Peers on a mesh you joined behaving badly. A private mesh is joined by invitation, so every
  peer was let in deliberately. Trust scoring does not exist yet, on purpose.
