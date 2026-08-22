// Everything this bridge knows about OpenSpec is a command it runs and a shape it reads back.
//
// Nothing here works out which artifact comes next, whether a change is finished, or what a delta
// merges to. Those are what OpenSpec is, and a second implementation of its state model would
// drift from it and then be wrong in a way nobody notices until it matters. Where a field is read
// rather than computed, the comment says which command reported it.

import { spawn } from 'node:child_process';
import { readFileSync } from 'node:fs';
import { readFile, readdir } from 'node:fs/promises';
import path from 'node:path';
import { fileURLToPath } from 'node:url';

/** How long any one CLI call may take before it is given up on. */
const TIMEOUT_MS = 60_000;

/** Where OpenSpec keeps changes that have been folded back into the specs. */
const ARCHIVE_DIR = path.join('openspec', 'changes', 'archive');

/**
 * Runs the OpenSpec CLI and returns what it printed.
 *
 * Through the local install rather than through npx, so a run cannot pause to fetch a package
 * halfway through answering a tool call.
 */
export function runOpenSpec(args, cwd) {
  return new Promise((resolve, reject) => {
    const child = spawn(process.execPath, [resolveCli(), ...args], {
      cwd,
      stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: true
    });

    let out = '';
    let err = '';

    const timer = setTimeout(() => {
      child.kill();
      reject(new Error(`openspec ${args[0]} took longer than ${TIMEOUT_MS / 1000} seconds.`));
    }, TIMEOUT_MS);

    child.stdout.on('data', chunk => { out += chunk; });
    child.stderr.on('data', chunk => { err += chunk; });

    child.on('error', error => {
      clearTimeout(timer);
      reject(new Error(`openspec could not be started: ${error.message}`));
    });

    child.on('close', code => {
      clearTimeout(timer);

      if (code !== 0) {
        reject(new Error(`openspec ${args.join(' ')} failed: ${(err || out).trim().slice(0, 400)}`));
        return;
      }

      resolve(out);
    });
  });
}

/**
 * The CLI entry point inside the dependency.
 *
 * Read out of the dependency's own package.json rather than written down here. Guessing the path
 * put it at dist/cli/index.js, which is where the code is and is not what the package declares as
 * its bin; it declares bin/openspec.js, and a hardcoded guess would have broken on the first
 * version that moved a file.
 *
 * Through fileURLToPath rather than by trimming the URL's pathname by hand, because on Windows
 * that pathname begins with a slash before the drive letter and percent encodes anything that
 * needed it.
 */
function resolveCli() {
  const here = path.dirname(fileURLToPath(import.meta.url));
  const packageRoot = path.join(here, '..', 'node_modules', '@fission-ai', 'openspec');

  let declared = './bin/openspec.js';

  try {
    const manifest = JSON.parse(readFileSync(path.join(packageRoot, 'package.json'), 'utf8'));

    declared = typeof manifest.bin === 'string' ? manifest.bin : manifest.bin?.openspec ?? declared;
  } catch {
    // A dependency whose manifest will not read still has a conventional bin, and the spawn below
    // reports plainly if it is not there.
  }

  return path.join(packageRoot, declared);
}

/** Runs a command that prints JSON and parses it. */
export async function json(args, cwd) {
  const text = await runOpenSpec([...args, '--json'], cwd);

  try {
    return JSON.parse(text);
  } catch {
    throw new Error(`openspec ${args.join(' ')} printed something that was not JSON.`);
  }
}

/** What the tool is and where it decided its root is. */
export async function describe(cwd) {
  const version = (await runOpenSpec(['--version'], cwd)).trim();

  // openspec list reports the resolved root alongside the changes, which is the cheapest way to
  // ask where it thinks it is looking.
  let root = null;

  try {
    root = (await json(['list'], cwd))?.root?.path ?? null;
  } catch {
    // A folder OpenSpec has not been initialised in has no root, which is worth reporting as
    // nothing rather than as a failure.
  }

  return { tool: 'OpenSpec', version, root };
}

/**
 * Every change, active and archived, with the state of each artifact.
 *
 * Active ones come from `openspec list --json`. Archived ones do not: list reports only what is
 * still being worked on, so the archive folder is read directly. That is reading the layout
 * OpenSpec documents rather than deciding anything about it.
 */
export async function changes(cwd) {
  const listed = await json(['list'], cwd);
  const result = [];

  for (const change of listed.changes ?? []) {
    result.push(await describeChange(change.name, 'active', cwd));
  }

  for (const name of await archived(cwd)) {
    result.push({
      id: name,
      name,
      status: 'archived',

      // Status is not asked for an archived change: it lives outside the changes directory the
      // CLI resolves against, and asking would report it as missing rather than as finished.
      artifacts: []
    });
  }

  return result;
}

/** One change with its artifacts, as `openspec status` reports them. */
async function describeChange(name, status, cwd) {
  const artifacts = [];

  try {
    const reported = await json(['status', '--change', name], cwd);

    for (const artifact of reported.artifacts ?? []) {
      artifacts.push({
        id: artifact.id,
        name: artifact.id,

        // Read, never derived. done, ready and blocked are exactly the words the CLI uses.
        state: artifact.status,
        detail: detailFor(artifact)
      });
    }
  } catch (error) {
    // A change the CLI will not report on is a change with no artifacts and a reason, which is
    // more useful than the whole list failing because one of them is malformed.
    artifacts.push({
      id: 'unknown',
      name: 'could not be read',
      state: 'blocked',
      detail: error.message
    });
  }

  return { id: name, name, status, artifacts };
}

/** Why an artifact is where it is, when the CLI said. */
function detailFor(artifact) {
  if (artifact.missingDeps?.length) {
    return `Waiting on ${artifact.missingDeps.join(', ')}.`;
  }

  if (artifact.outputPath) {
    return artifact.outputPath;
  }

  return null;
}

/** The names of archived changes, or none when there is no archive. */
async function archived(cwd) {
  try {
    const entries = await readdir(path.join(cwd, ARCHIVE_DIR), { withFileTypes: true });

    return entries.filter(e => e.isDirectory()).map(e => e.name);
  } catch {
    return [];
  }
}

/**
 * The text of one artifact.
 *
 * The path comes from `openspec status`, which reports where each artifact goes and which files
 * exist for it, so nothing here has to know the layout. An artifact spread across several files,
 * which is what specs are, is joined with its file names so a reader can tell them apart.
 */
export async function artifact(changeName, artifactId, cwd) {
  const reported = await json(['status', '--change', changeName], cwd);
  const paths = reported.artifactPaths?.[artifactId];

  if (!paths) {
    throw new Error(`${changeName} has no artifact called ${artifactId}.`);
  }

  const existing = paths.existingOutputPaths ?? [];

  if (existing.length === 0) {
    return {
      content: '',
      path: paths.outputPath ?? null
    };
  }

  const parts = [];

  for (const file of existing) {
    const text = await readFile(file, 'utf8');

    parts.push(existing.length === 1
      ? text
      : `<!-- ${path.relative(reported.changeRoot ?? cwd, file).replace(/\\/g, '/')} -->\n\n${text}`);
  }

  return {
    content: parts.join('\n\n'),
    path: existing.length === 1 ? existing[0] : paths.outputPath ?? null
  };
}

/**
 * What to do next for a change.
 *
 * Not what v1.43's contract assumed. OpenSpec has no command that writes an artifact: creating one
 * is an agent's job, driven by the instructions the CLI hands out. So this returns those
 * instructions and the template, which is the whole of what OpenSpec offers here, and says plainly
 * that the writing is still to be done.
 */
export async function advance(changeName, cwd) {
  const reported = await json(['status', '--change', changeName], cwd);
  const next = (reported.artifacts ?? []).find(a => a.status === 'ready');

  if (!next) {
    const message = reported.isPlanningComplete
      ? `Every planning artifact for ${changeName} is written. What is left is doing the work in tasks.md.`
      : `Nothing in ${changeName} is ready to write. Something it depends on has to be written first.`;

    return { message, change: await describeChange(changeName, 'active', cwd) };
  }

  const guidance = await json(['instructions', next.id, '--change', changeName], cwd);

  const message = [
    `${next.id} is the next artifact to write, at ${guidance.outputPath}.`,
    guidance.description ? `\n${guidance.description}` : '',
    guidance.instruction ? `\n\n${guidance.instruction}` : '',
    guidance.template ? `\n\nTemplate:\n\n${guidance.template}` : '',
    '\n\nOpenSpec does not write this itself. Send it to the Workspace, or write it in your editor, and refresh.'
  ].join('');

  return { message, change: await describeChange(changeName, 'active', cwd) };
}
