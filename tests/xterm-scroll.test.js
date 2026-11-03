'use strict';

const assert = require('node:assert/strict');
const test = require('node:test');

// The bundled browser build needs these globals when loaded without a DOM.
globalThis.self = globalThis;
globalThis.window = globalThis;
globalThis.document = { createElement: () => ({ getContext: () => null }) };
const { Terminal } = require('../WindowsApp/Assets/xterm/xterm.js');

const write = (term, data) => new Promise(resolve => term.write(new TextEncoder().encode(data), resolve));

async function run(command, count, cursorRow = 3, prefix = '') {
  const term = new Terminal({ cols: 10, rows: 6, allowProposedApi: true });
  try {
    await write(term, `${prefix}A\r\nB\r\nC\r\nD\r\nE\r\nF`);
    await write(term, `\x1b[2;5r\x1b[${cursorRow};1H`);
    // Split the sequence as SSH data can arrive in separate chunks.
    await write(term, `\x1b[${count}`);
    await write(term, command);
    const buffer = term.buffer.active;
    return {
      lines: Array.from({ length: 6 }, (_, i) => buffer.getLine(buffer.baseY + i).translateToString(true)),
      cursor: [buffer.cursorX, buffer.cursorY],
      baseY: buffer.baseY
    };
  } finally {
    term.dispose();
  }
}

const expected = {
  S: { one: ['A', 'C', 'D', 'E', '', 'F'], two: ['A', 'D', 'E', '', '', 'F'], full: ['A', '', '', '', '', 'F'] },
  T: { one: ['A', '', 'B', 'C', 'D', 'F'], two: ['A', '', '', 'B', 'C', 'F'], full: ['A', '', '', '', '', 'F'] },
  L: { one: ['A', 'B', '', 'C', 'D', 'F'], two: ['A', 'B', '', '', 'C', 'F'], full: ['A', 'B', '', '', '', 'F'] },
  M: { one: ['A', 'B', 'D', 'E', '', 'F'], two: ['A', 'B', 'E', '', '', 'F'], full: ['A', 'B', '', '', '', 'F'] }
};

for (const [command, result] of Object.entries(expected)) {
  test(`${command} respects counts and scroll-region boundaries`, async () => {
    for (const [count, lines] of [['', result.one], ['0', result.one], ['1', result.one], ['2', result.two], [command === 'S' || command === 'T' ? '4' : '3', result.full], ['10', result.full]]) {
      assert.deepEqual(await run(command, count), { lines, cursor: [0, 2], baseY: 0 }, `count ${count}`);
    }
    if (command === 'L' || command === 'M') {
      assert.deepEqual(await run(command, '10', 1), { lines: ['A', 'B', 'C', 'D', 'E', 'F'], cursor: [0, 0], baseY: 0 });
      assert.deepEqual(await run(command, '10', 6), { lines: ['A', 'B', 'C', 'D', 'E', 'F'], cursor: [0, 5], baseY: 0 });
    }
  });
  test(`${command} handles extreme counts`, async () => {
    assert.deepEqual(await run(command, '2147483647', 3, 'history\r\n'), { lines: result.full, cursor: [0, 2], baseY: 1 });
    assert.deepEqual(await run(command, '2147483647', 3, '\x1b[?1049h'), { lines: result.full, cursor: [0, 2], baseY: 0 });
  });
}
