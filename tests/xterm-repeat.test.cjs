const assert = require('node:assert/strict');
const test = require('node:test');

// Node 21+ exposes navigator, but the bundled browser build detects Node by its absence.
delete globalThis.navigator;
globalThis.self = globalThis;
const { Terminal } = require('../WindowsApp/Assets/xterm/xterm.js');
const encoder = new TextEncoder();
const limit = 1024;

function write(term, data) {
  return new Promise(resolve => term.write(encoder.encode(data), resolve));
}

function contents(term) {
  const buffer = term.buffer.active;
  return Array.from({ length: buffer.length }, (_, i) =>
    buffer.getLine(i).translateToString(true)).join('');
}

test('REP preserves default counts, normal output, wrapping, and following input', async () => {
  const term = new Terminal({ cols: 10, rows: 4 });
  try {
    await write(term, '\x1b[100bA\x1b[bB\x1b[0bC\x1b[12b!');
    assert.equal(contents(term), 'AABBC' + 'C'.repeat(12) + '!');
  } finally {
    term.dispose();
  }
});

test('REP retains counts at and below the limit', async () => {
  for (const count of [limit - 1, limit]) {
    const term = new Terminal({ cols: 80, rows: 24 });
    try {
      await write(term, `X\x1b[${count}b!`);
      assert.equal(contents(term), 'X'.repeat(count + 1) + '!');
    } finally {
      term.dispose();
    }
  }
});

test('REP supports split input and clamps oversized counts', async () => {
  const term = new Terminal({ cols: 80, rows: 24 });
  try {
    await write(term, 'X\x1b[10');
    await write(term, '25b!');
    assert.equal(contents(term), 'X'.repeat(limit + 1) + '!');
  } finally {
    term.dispose();
  }
});

test('REP bounds allocation and work even for the largest parser parameter', () => {
  const term = new Terminal({ cols: 80, rows: 24 });
  const input = term._core._inputHandler;
  const originalPrint = input.print;
  const originalUint32Array = globalThis.Uint32Array;
  const printLengths = [];
  input.print = function (data, start, end) {
    printLengths.push(end - start);
    return originalPrint.call(this, data, start, end);
  };
  globalThis.Uint32Array = new Proxy(originalUint32Array, {
    construct(target, args) {
      if (typeof args[0] === 'number' && args[0] > limit) {
        throw new Error(`Unexpected Uint32Array allocation: ${args[0]}`);
      }
      return Reflect.construct(target, args);
    }
  });
  try {
    input.parse(encoder.encode('X\x1b[2147483647b!'));
    assert.deepEqual(printLengths, [1, limit, 1]);
    assert.equal(contents(term), 'X'.repeat(limit + 1) + '!');
  } finally {
    globalThis.Uint32Array = originalUint32Array;
    term.dispose();
  }
});
