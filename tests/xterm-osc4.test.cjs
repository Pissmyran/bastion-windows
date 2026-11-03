const test = require('node:test');
const assert = require('node:assert/strict');

// xterm's bundled browser build uses navigator to detect Node.
global.self = globalThis;
delete global.navigator;
const { Terminal } = require('../WindowsApp/Assets/xterm/xterm.js');

function write(term, data) {
  return new Promise(resolve => term.write(new TextEncoder().encode(data), resolve));
}

function createTerminal() {
  const term = new Terminal();
  const events = [];
  term._core._inputHandler.onColor(event => events.push(...event));
  return { term, events };
}

test('OSC 4 preserves valid sets and queries in order, ignoring invalid and incomplete pairs', async () => {
  const { term, events } = createTerminal();
  try {
    await write(term, '\x1b]4;1;#abc;2;?;256;?;bad;#fff;3;bad;2;rgb:ff/00/80;4\x07');
    assert.deepEqual(events, [
      { type: 1, index: 1, color: [160, 176, 192] },
      { type: 0, index: 2 },
      { type: 1, index: 2, color: [255, 0, 128] },
    ]);
  } finally { term.dispose(); }
});

test('OSC 4 processes at most 256 pairs and resumes parsing subsequent output', async () => {
  const { term, events } = createTerminal();
  try {
    const pairs = Array.from({ length: 256 }, (_, i) => `${i};?`).join(';');
    await write(term, `\x1b]4;${pairs};1;#fff\x07OK\x1b]4;3;?\x07`);
    assert.equal(events.length, 257);
    assert.deepEqual(events.slice(0, 256), Array.from({ length: 256 }, (_, index) => ({ type: 0, index })));
    assert.deepEqual(events[256], { type: 0, index: 3 });
    assert.equal(term.buffer.active.getLine(0).translateToString(true), 'OK');
  } finally { term.dispose(); }
});

test('invalid pairs count toward the limit across chunk boundaries', async () => {
  const { term, events } = createTerminal();
  try {
    const payload = `\x1b]4;${'bad;?;'.repeat(255)}7;?;8;?\x1b\\`;
    await write(term, payload.slice(0, 300));
    assert.equal(events.length, 0);
    await write(term, payload.slice(300, -2));
    assert.equal(events.length, 0);
    await write(term, payload.slice(-2));
    assert.deepEqual(events, [{ type: 0, index: 7 }]);
  } finally { term.dispose(); }
});

test('large sequences do not generate unbounded color events', async () => {
  const { term, events } = createTerminal();
  try {
    await write(term, `\x1b]4;${'1;?;'.repeat(20000)}2;?\x07`);
    assert.equal(events.length, 256);
    assert.ok(events.every(event => event.type === 0 && event.index === 1));
  } finally { term.dispose(); }
});

test('OSC 4 applies sets and reports the resulting colors in order', async () => {
  const { term } = createTerminal();
  const colors = { ansi: Array.from({ length: 256 }, () => ({ rgba: 0x123456ff })) };
  term._core._themeService = { colors, modifyColors: callback => callback(colors) };
  const replies = [];
  term.onData(data => replies.push(data));
  try {
    await write(term, '\x1b]4;1;#ff0000;1;?;1;#00ff00;1;?\x07');
    assert.deepEqual(replies, [
      '\x1b]4;1;rgb:ffff/0000/0000\x1b\\',
      '\x1b]4;1;rgb:0000/ffff/0000\x1b\\',
    ]);
  } finally { term.dispose(); }
});
