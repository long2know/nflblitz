export function installViewControls(document = globalThis.document) {
  const window = document.defaultView;
  const viewport = document.querySelector('#game-viewport'), canvas = document.querySelector('#game');
  const toggle = document.querySelector('#game-view-toggle'), fullscreen = document.querySelector('#game-fullscreen');
  const status = document.querySelector('#status'), message = document.querySelector('#game-view-message');
  let available = false, gameOnly = false, wanted = false, native = false, pending = false, exiting = false;
  let scroll, statusClass;
  const focusGame = () => { if (available) canvas.focus({ preventScroll: true }); };
  const tell = text => { message.textContent = text; };
  const syncButtons = () => {
    toggle.disabled = !available && !gameOnly;
    toggle.textContent = gameOnly ? 'Exit game view' : 'Game-only';
    toggle.setAttribute('aria-pressed', String(gameOnly));
    fullscreen.disabled = pending || exiting || (!available && !native);
    fullscreen.textContent = native ? 'Exit fullscreen' : 'Fullscreen';
    fullscreen.setAttribute('aria-pressed', String(native));
  };
  const setView = enabled => {
    if (enabled !== gameOnly) {
      if (enabled) scroll = [window.scrollX, window.scrollY];
      gameOnly = enabled;
      document.documentElement.classList.toggle('game-only', enabled);
      if (!enabled && scroll) window.scrollTo(...scroll);
    }
    syncButtons();
    focusGame();
  };
  const leave = async () => {
    wanted = false;
    if (exiting) return;
    if (document.fullscreenElement === viewport) {
      exiting = true; syncButtons();
      try { await document.exitFullscreen(); }
      catch {
        setView(true);
        tell('Could not exit fullscreen. Use the browser’s fullscreen exit control, then Exit game view.');
        return;
      } finally { exiting = false; syncButtons(); }
    }
    setView(false);
  };
  const syncFullscreen = () => {
    const active = document.fullscreenElement === viewport, changed = active !== native;
    native = active; syncButtons();
    if (native && (!wanted || !available)) { void leave(); return; }
    if (native) setView(true);
    else if (changed) focusGame();
  };
  const changeFullscreen = async () => {
    if (pending || exiting) return;
    tell('');
    if (document.fullscreenElement === viewport) {
      try { await document.exitFullscreen(); }
      catch { tell('Could not exit fullscreen. Use the browser’s fullscreen exit control.'); }
      syncFullscreen(); focusGame();
      return;
    }
    if (!available) return;
    wanted = true; setView(true);
    if (typeof viewport.requestFullscreen !== 'function' || document.fullscreenEnabled === false ||
        typeof document.exitFullscreen !== 'function') {
      tell('Fullscreen is unavailable in this browser or page. Game-only still fits the window.');
      return;
    }
    pending = true; syncButtons();
    try {
      // Call directly from the button gesture; never await before requesting fullscreen.
      await viewport.requestFullscreen();
    } catch {
      tell('Fullscreen was denied or could not start. Game-only still fits the window.');
    } finally {
      pending = false; syncFullscreen(); focusGame();
    }
  };
  const changeView = () => {
    tell('');
    if (gameOnly) void leave();
    else if (available) { wanted = true; setView(true); }
  };
  const syncReady = () => {
    if (status.className === statusClass) return;
    statusClass = status.className;
    const observed = status.classList.contains('diagnostic') ? window.sourcePlayer?.observe() : null;
    // Real cabinet menus precede gameplay Ready; their diagnostic remains visible.
    const interactive = observed?.failed === false && observed?.state?.kernel?.portableBound === true &&
      observed.state.actorCount === 14 && observed.state.input != null;
    const next = !window.sourcePlayerLoader?.failed && (status.classList.contains('ready') || interactive);
    if (next === available) return;
    available = next;
    tell(available ? '' : 'Game view controls unlock when the game is ready.');
    if (!available && (gameOnly || pending)) void leave();
    syncButtons();
  };
  const fullscreenError = () => {
    if (!message.textContent) tell('Could not change fullscreen. Game-only remains available.');
  };
  const buttonKey = event => {
    // Let native button activation work without sending Enter to the cabinet's window listener.
    if (event.code === 'Enter' || event.code === 'Space') event.stopPropagation();
  };
  toggle.addEventListener('click', changeView);
  fullscreen.addEventListener('click', changeFullscreen);
  for (const button of [toggle, fullscreen]) {
    button.addEventListener('keydown', buttonKey);
    button.addEventListener('keyup', buttonKey);
  }
  document.addEventListener('fullscreenchange', syncFullscreen);
  document.addEventListener('fullscreenerror', fullscreenError);
  const observer = new window.MutationObserver(syncReady);
  observer.observe(status, { attributes: true, attributeFilter: ['class'] });
  syncReady(); syncButtons();
  return () => {
    observer.disconnect();
    toggle.removeEventListener('click', changeView);
    fullscreen.removeEventListener('click', changeFullscreen);
    for (const button of [toggle, fullscreen]) {
      button.removeEventListener('keydown', buttonKey);
      button.removeEventListener('keyup', buttonKey);
    }
    document.removeEventListener('fullscreenchange', syncFullscreen);
    document.removeEventListener('fullscreenerror', fullscreenError);
  };
}
