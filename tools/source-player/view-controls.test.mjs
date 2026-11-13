import assert from 'node:assert/strict';
import test from 'node:test';
import { installViewControls } from './wwwroot/view-controls.js';

class Element extends EventTarget {
  constructor(document) {
    super();
    this.document = document; this.attributes = new Map(); this.disabled = false;
    this.textContent = ''; this.classes = new Set();
    this.classList = {
      contains: name => this.classes.has(name),
      toggle: (name, enabled) => enabled ? this.classes.add(name) : this.classes.delete(name),
    };
  }
  get className() { return [...this.classes].join(' '); }
  setAttribute(name, value) { this.attributes.set(name, value); }
  getAttribute(name) { return this.attributes.get(name); }
  focus(options) { this.document.activeElement = this; this.focusOptions = options; }
  click() { if (!this.disabled) this.dispatchEvent(new Event('click')); }
}

function fixture() {
  const document = new EventTarget(), requests = [], exits = [];
  let observer;
  document.defaultView = {
    scrollX: 0, scrollY: 180,
    scrollTo(x, y) { this.scrollX = x; this.scrollY = y; },
    MutationObserver: class {
      constructor(callback) { observer = this; this.callback = callback; }
      observe(element, options) { this.element = element; this.options = options; }
      disconnect() {}
    },
  };
  document.documentElement = new Element(document);
  const ids = ['game-viewport', 'game', 'game-view-toggle', 'game-fullscreen', 'status', 'game-view-message'];
  const elements = Object.fromEntries(ids.map(id => [id, new Element(document)]));
  document.querySelector = selector => elements[selector.slice(1)];
  document.fullscreenEnabled = true; document.fullscreenElement = null;
  const actualFullscreen = value => {
    document.fullscreenElement = value ? elements['game-viewport'] : null;
    document.dispatchEvent(new Event('fullscreenchange'));
  };
  elements['game-viewport'].requestFullscreen = () => {
    requests.push('request');
    actualFullscreen(true);
    return Promise.resolve();
  };
  document.exitFullscreen = () => {
    exits.push('exit');
    actualFullscreen(false);
    return Promise.resolve();
  };
  const dispose = installViewControls(document);
  return {
    document, elements, requests, exits, dispose, actualFullscreen,
    ready(value = true) {
      elements.status.classList.toggle('ready', value);
      observer.callback();
    },
    diagnostic(observed) {
      document.defaultView.sourcePlayer = { observe: () => observed };
      elements.status.classList.toggle('diagnostic', true);
      observer.callback();
    },
    inView: () => document.documentElement.classList.contains('game-only'),
    toggle: () => elements['game-view-toggle'].click(),
    fullscreen: () => elements['game-fullscreen'].click(),
  };
}
const settle = () => new Promise(resolve => setImmediate(resolve));

test('startup cannot hide audio/progress; actual Ready unlocks a reversible, canvas-focused game view', () => {
  const f = fixture(), { document, elements } = f;
  assert.equal(elements['game-view-toggle'].disabled, true);
  assert.equal(elements['game-fullscreen'].disabled, true);
  f.toggle(); f.fullscreen();
  assert.equal(f.inView(), false);
  assert.deepEqual(f.requests, []);
  f.ready();
  f.toggle();
  assert.equal(f.inView(), true);
  assert.equal(elements['game-view-toggle'].textContent, 'Exit game view');
  assert.equal(elements['game-view-toggle'].getAttribute('aria-pressed'), 'true');
  assert.equal(document.activeElement, elements.game);
  assert.deepEqual(elements.game.focusOptions, { preventScroll: true });
  document.defaultView.scrollY = 0;
  f.toggle();
  assert.equal(f.inView(), false);
  assert.equal(document.defaultView.scrollY, 180);
  assert.equal(elements['game-view-toggle'].textContent, 'Game-only');
  assert.equal(elements['game-view-toggle'].getAttribute('aria-pressed'), 'false');
  assert.equal(document.activeElement, elements.game);
});

test('native request is synchronous with the click; actual fullscreenchange, including browser exit, owns the label', async () => {
  const f = fixture();
  f.ready(); f.fullscreen();
  assert.deepEqual(f.requests, ['request'], 'Do not defer the gesture-gated native request.');
  assert.equal(f.document.fullscreenElement, f.elements['game-viewport']);
  assert.equal(f.elements['game-fullscreen'].textContent, 'Exit fullscreen');
  await settle();
  f.actualFullscreen(false);
  assert.equal(f.elements['game-fullscreen'].getAttribute('aria-pressed'), 'false');
  assert.equal(f.elements['game-fullscreen'].textContent, 'Fullscreen');
  assert.equal(f.inView(), true, 'A browser-native exit should retain the independent window-fitted mode.');
  assert.equal(f.document.activeElement, f.elements.game);
  f.toggle();
  assert.equal(f.inView(), false);
});

test('real bound C# pregame can use game view without forging qualified Ready; strict diagnostics cannot', () => {
  const strict = fixture();
  strict.diagnostic({ failed: false, state: { kernel: { portableBound: false }, actorCount: 14, input: {} } });
  assert.equal(strict.elements['game-view-toggle'].disabled, true);
  const f = fixture();
  const observed = { ready: false, failed: false,
    state: { kernel: { portableBound: true }, actorCount: 14, input: {} } };
  f.diagnostic(observed);
  f.toggle();
  assert.equal(f.inView(), true);
  assert.equal(observed.ready, false);
  assert.equal(f.elements.status.classList.contains('diagnostic'), true);
  assert.equal(f.elements.status.classList.contains('ready'), false);
});

test('unsupported and denied fullscreen leave a readable, usable game-only fallback', async () => {
  for (const unsupported of ['missing-api', 'page-policy']) {
    const f = fixture();
    if (unsupported === 'missing-api') f.elements['game-viewport'].requestFullscreen = undefined;
    else f.document.fullscreenEnabled = false;
    f.ready(); f.fullscreen();
    assert.equal(f.inView(), true);
    assert.deepEqual(f.requests, []);
    assert.match(f.elements['game-view-message'].textContent, /unavailable.*Game-only/);
    assert.equal(f.elements['game-fullscreen'].getAttribute('aria-pressed'), 'false');
    f.toggle(); assert.equal(f.inView(), false);
  }
  const f = fixture();
  f.elements['game-viewport'].requestFullscreen = () => Promise.reject(new Error('Denied'));
  f.ready(); f.fullscreen(); await settle();
  assert.equal(f.inView(), true);
  assert.match(f.elements['game-view-message'].textContent, /denied.*Game-only/);
  const reason = f.elements['game-view-message'].textContent;
  f.document.dispatchEvent(new Event('fullscreenerror'));
  assert.equal(f.elements['game-view-message'].textContent, reason,
    'A later native fullscreenerror must not overwrite the readable request-denied reason.');
  assert.equal(f.elements['game-fullscreen'].disabled, false);
  assert.equal(f.document.activeElement, f.elements.game);
});

test('Exit game view exits real fullscreen; an exit rejection keeps the exit control and actual state honest', async () => {
  const f = fixture();
  f.ready(); f.fullscreen(); await settle();
  f.document.exitFullscreen = () => Promise.reject(new Error('Exit denied'));
  f.toggle(); await settle();
  assert.equal(f.inView(), true);
  assert.equal(f.elements['game-view-toggle'].disabled, false);
  assert.equal(f.elements['game-fullscreen'].getAttribute('aria-pressed'), 'true');
  assert.match(f.elements['game-view-message'].textContent, /Could not exit fullscreen/);
  f.document.exitFullscreen = () => { f.actualFullscreen(false); return Promise.resolve(); };
  f.toggle(); await settle();
  assert.equal(f.document.fullscreenElement, null);
  assert.equal(f.inView(), false);
  assert.equal(f.document.activeElement, f.elements.game);
});

test('exit during a pending native request is not undone by the late native entry', async () => {
  const f = fixture();
  let finish;
  f.elements['game-viewport'].requestFullscreen = () => new Promise(resolve => { finish = resolve; });
  f.ready(); f.fullscreen();
  assert.equal(f.inView(), true);
  assert.equal(f.elements['game-view-toggle'].disabled, false);
  f.toggle();
  assert.equal(f.inView(), false);
  f.actualFullscreen(true); finish(); await settle();
  assert.equal(f.document.fullscreenElement, null);
  assert.equal(f.inView(), false);
});

test('a later loader failure restores the diagnostic page, including native fullscreen exit', async () => {
  const f = fixture();
  f.ready(); f.fullscreen(); await settle();
  f.ready(false); await settle();
  assert.equal(f.document.fullscreenElement, null);
  assert.equal(f.inView(), false);
  assert.equal(f.elements['game-view-toggle'].disabled, true);
});

test('only focused button activation is isolated; F/R/Escape and cabinet keys are not view shortcuts', () => {
  const f = fixture();
  for (const target of [f.elements['game-view-toggle'], f.elements['game-fullscreen']]) {
    for (const code of ['Enter', 'Space', 'KeyF', 'KeyR', 'Escape', 'KeyB', 'KeyX']) {
      for (const type of ['keydown', 'keyup']) {
        const event = new Event(type, { cancelable: true });
        Object.defineProperty(event, 'code', { value: code });
        target.dispatchEvent(event);
        assert.equal(event.defaultPrevented, false, 'Native button behavior must remain intact.');
        assert.equal(event.cancelBubble, code === 'Enter' || code === 'Space', `${type} ${code}`);
      }
    }
  }
  assert.equal(f.inView(), false);
  assert.deepEqual(f.requests, []);
});
