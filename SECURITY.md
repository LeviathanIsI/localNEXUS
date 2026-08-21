# Security policy

## Reporting a vulnerability

**Please do not open a public issue.**

Report privately through GitHub:
[open a security advisory](https://github.com/You-Know-Its-Me-Studios/LocalNEXUS/security/advisories/new).
That gives us a private thread and, if it turns out to be real, a way to publish an advisory
and credit you.

If GitHub advisories are not available to you, email joshua.r.bradford1@gmail.com with
"LocalNEXUS security" in the subject.

Please include enough to reproduce it: the version, what an attacker would have to control,
and what they get. Proof of concept code is welcome.

This is a small project with one maintainer, so set your expectations accordingly. You should
get an acknowledgement within a week. If a report is valid, the fix ships in the next release
and the advisory goes out with it. If you disagree with an assessment, say so; it will not be
held against you.

## Supported versions

Only the latest release. LocalNEXUS is pre-1.0 and there are no maintenance branches.

## What this application actually does

Worth knowing before you decide what counts as a vulnerability, and worth knowing before you
run it on a machine you care about. None of the following is a secret or a bug. It is what
the tool is.

**It runs code that a language model wrote.** Files are written into your project and, if it
is a Unity project, Unity will compile and run them. The compile check verifies that code
builds, not that it is safe. Review what gets written, particularly if a model came from
somewhere you do not control.

**A Patch node executes arbitrary C#.** In script mode it compiles an expression from the
graph with Roslyn and runs it in this process, with this process's privileges. Roslyn
scripting is not a sandbox and is not intended to be one.

**A graph file is therefore executable content, not data.** Opening a `.nexusgraph.json`
someone sent you is equivalent to running a program they sent you. Treat it that way. There
is currently no warning in the interface about this, which is itself worth fixing.

**API keys are stored in plain text.** Both in `config.json` and inside any saved graph that
has a Model node configured for a hosted provider. If you share a graph, or commit one, your
key goes with it. Strip the `apiKey` fields first.

**It starts child processes and opens ports.** `llama-server` binds to `127.0.0.1` on a port
chosen per model, so it is not reachable from the network. It is started with permissive CORS
and no API key, which is safe only because of that loopback binding. The mesh node, when you
enable it, listens on a configurable port, defaulting to 9337, and discovery is scoped to the
local network unless you explicitly opt in to publishing.

**Engine processes are owned through Windows job objects**, so the operating system
terminates them when the application's handle closes. The application never terminates a
process it did not start.

**It writes into your filesystem.** The Output node resolves paths through the project
service, which refuses anything landing outside the opened project folder. A way to escape
that is a vulnerability and worth reporting.

## What is in scope

- Escaping the project folder when writing files.
- Reaching the local inference server or the mesh node from outside the machine when it
  should not be reachable.
- Anything that makes opening a graph, a project or a model file more dangerous than the
  above already describes.
- Leaking API keys anywhere beyond the plain text storage described above.
- Privilege escalation, or an engine process outliving the application in a way the job
  object was supposed to prevent.

## What is not in scope

- A model producing bad, insecure or malicious code. That is what the compile check and your
  review are for, and it is why the generated code is shown to you before anything runs.
- Anything requiring an attacker to already have code execution on your machine.
- Vulnerabilities in llama.cpp, Mesh LLM, uv, torch or transformers. Report those upstream;
  we will pick up their fixes. Tell us anyway if we are shipping a version that is affected.
- Peers on a mesh you joined behaving badly. A private mesh is joined by invitation, so every
  peer in it was let in deliberately. Trust scoring does not exist yet, on purpose.
