#!/usr/bin/env node

// The LocalNEXUS spec contract, spoken over stdio, answered by asking the OpenSpec CLI.
//
// Newline delimited JSON-RPC 2.0, the same framing the node contract and MCP use. Four calls and
// one notification:
//
//   spec/describe   what this is bridging to, its version, and the root it resolved
//   spec/changes    every change with its artifacts and the state of each
//   spec/artifact   the text of one artifact, and where it lives
//   spec/advance    what to write next, with OpenSpec's own instructions for it
//   spec/log        sent to the host, and reaches the activity feed
//
// Nothing but the protocol is ever written to stdout. A stray line there is a parse error at the
// other end, which is why anything worth saying goes to stderr or through spec/log.

import { createInterface } from 'node:readline';

import { advance, artifact, changes, describe } from './openspec.js';

const cwd = process.env.LOCALNEXUS_PROJECT ?? process.cwd();

const reader = createInterface({ input: process.stdin, crlfDelay: Infinity });

reader.on('line', line => {
  const text = line.trim();

  if (text.length === 0) {
    return;
  }

  let message;

  try {
    message = JSON.parse(text);
  } catch {
    // A line that is not JSON has no id to answer against, so there is nobody to tell.
    log('A line arrived that was not JSON and was discarded.');
    return;
  }

  handle(message);
});

// Stdin closing means the host has finished asking, not that an answer in flight may be dropped.
// Exiting on close alone lost the reply to the last call every time, because a call is
// asynchronous and closing is not.
let inFlight = 0;
let closed = false;

reader.on('close', () => {
  closed = true;
  exitWhenIdle();
});

function exitWhenIdle() {
  if (closed && inFlight === 0) {
    process.exit(0);
  }
}

async function handle(message) {
  const { id, method, params } = message ?? {};

  // A notification has no id and wants no answer.
  if (id === undefined || id === null) {
    return;
  }

  inFlight += 1;

  try {
    send({ jsonrpc: '2.0', id, result: await dispatch(method, params ?? {}) });
  } catch (error) {
    send({
      jsonrpc: '2.0',
      id,
      error: { code: -32000, message: error?.message ?? String(error) }
    });
  } finally {
    inFlight -= 1;
    exitWhenIdle();
  }
}

function dispatch(method, params) {
  switch (method) {
    case 'spec/describe':
      return describe(cwd);

    case 'spec/changes':
      return changes(cwd).then(list => ({ changes: list }));

    case 'spec/artifact':
      return require2(params, ['changeId', 'artifactId'])
        .then(() => artifact(params.changeId, params.artifactId, cwd));

    case 'spec/advance':
      return require2(params, ['changeId']).then(() => advance(params.changeId, cwd));

    default:
      return Promise.reject(new Error(`This bridge has no method called ${method}.`));
  }
}

/** Refuses a call missing something it cannot proceed without, by name. */
function require2(params, names) {
  for (const name of names) {
    if (typeof params?.[name] !== 'string' || params[name].length === 0) {
      return Promise.reject(new Error(`${name} is required and was not given.`));
    }
  }

  return Promise.resolve();
}

/** A line for the host's activity feed. */
function log(message) {
  send({ jsonrpc: '2.0', method: 'spec/log', params: { message } });
}

function send(payload) {
  process.stdout.write(`${JSON.stringify(payload)}\n`);
}
