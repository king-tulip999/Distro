(() => {
  const w = window;
  if (!w.__monkey) {
    w.__monkey = { errors: [], seed: SEED };
    addEventListener('error', e => w.__monkey.errors.push(String(e.message) + ' @ ' + String(e.filename || '').split('/').pop() + ':' + e.lineno));
    addEventListener('unhandledrejection', e => w.__monkey.errors.push('rejection: ' + String(e.reason && e.reason.stack || e.reason)));
    const refuse = () => Promise.reject(new DOMException('refused', 'AbortError'));
    w.showOpenFilePicker = w.showSaveFilePicker = w.showDirectoryPicker = refuse;
    w.alert = () => {};
    w.confirm = () => Math.random() < 0.5;
    w.prompt = () => 'monkey';
    const click = HTMLInputElement.prototype.click;
    HTMLInputElement.prototype.click = function () { if (this.type !== 'file') click.call(this); };
    HTMLInputElement.prototype.showPicker = function () {};
    HTMLAnchorElement.prototype.click = function () {};
  }
  const m = w.__monkey;
  const rnd = n => { m.seed = (m.seed * 1103515245 + 12345) & 0x7fffffff; return m.seed % n; };
  const pick = list => list[rnd(list.length)];
  const shown = el => {
    const b = el.getBoundingClientRect();
    if (b.width < 2 || b.height < 2 || b.bottom < 0 || b.right < 0 || b.top > innerHeight || b.left > innerWidth) return false;
    const s = getComputedStyle(el);
    return s.visibility !== 'hidden' && s.display !== 'none' && s.pointerEvents !== 'none';
  };
  const find = sel => [...document.querySelectorAll(sel)].filter(e => !e.disabled && !(e.type === 'file') && shown(e));
  const name = el => el.tagName.toLowerCase() + (el.id ? '#' + el.id : '') + (el.classList[0] ? '.' + el.classList[0] : '') +
    ((el.innerText || el.value || el.title || '').trim() ? ' "' + (el.innerText || el.value || el.title).trim().slice(0, 30).replace(/\s+/g, ' ') + '"' : '');
  const at = (el, fx, fy) => { const b = el.getBoundingClientRect(); return { x: b.left + b.width * fx, y: b.top + b.height * fy }; };
  const fire = (el, type, p, extra) => {
    const init = Object.assign({ bubbles: true, cancelable: true, composed: true, clientX: p.x, clientY: p.y, button: 0, buttons: type.endsWith('up') || type === 'click' ? 0 : 1, pointerId: 1, pointerType: 'mouse', isPrimary: true }, extra || {});
    el.dispatchEvent(type.startsWith('pointer') ? new PointerEvent(type, init) : type === 'wheel' ? new WheelEvent(type, init) : new MouseEvent(type, init));
  };
  const tap = (el, p) => { fire(el, 'pointerdown', p); fire(el, 'mousedown', p); fire(el, 'pointerup', p); fire(el, 'mouseup', p); fire(el, 'click', p); };
  let done = 'nothing';
  const roll = rnd(100);
  if (roll < 38) {
    const el = pick(find('button,select,label,li,summary,[role=button],[tabindex],.chip,input[type=checkbox],input[type=radio]'));
    if (el) { tap(el, at(el, 0.5, 0.5)); done = 'click ' + name(el); }
  } else if (roll < 58) {
    const el = pick(find('canvas,.stage'));
    if (el) {
      const steps = 3 + rnd(8);
      let p = at(el, rnd(100) / 100, rnd(100) / 100);
      fire(el, 'pointerdown', p); fire(el, 'mousedown', p);
      for (let i = 0; i < steps; i++) {
        p = { x: p.x + rnd(81) - 40, y: p.y + rnd(81) - 40 };
        fire(el, 'pointermove', p); fire(document, 'mousemove', p); fire(w, 'pointermove', p);
      }
      fire(el, 'pointerup', p); fire(document, 'mouseup', p); fire(w, 'pointerup', p); fire(el, 'click', p);
      done = 'drag ' + steps + ' on ' + name(el);
    }
  } else if (roll < 74) {
    const el = pick(find('input:not([type=checkbox]):not([type=radio]):not([type=button]),textarea,select'));
    if (el) {
      if (el.tagName === 'SELECT') { if (el.options.length) el.selectedIndex = rnd(el.options.length); }
      else if (el.type === 'color') el.value = '#' + rnd(0xffffff).toString(16).padStart(6, '0');
      else if (el.type === 'number' || el.type === 'range') el.value = pick(['0', '-1', '1', '99', '1e9', '-1e9', '0.5', '', String(rnd(500))]);
      else el.value = pick(['abc', '', '   ', '12', 'NaN', '<b>x</b>', 'ÆØÅ ✓', 'a'.repeat(200), '#', '-']);
      el.focus();
      el.dispatchEvent(new Event('input', { bubbles: true }));
      el.dispatchEvent(new Event('change', { bubbles: true }));
      done = 'set ' + name(el) + ' = "' + String(el.value).slice(0, 20) + '"';
    }
  } else if (roll < 86) {
    const key = pick(['Delete', 'Backspace', 'Escape', 'Enter', 'z', 'y', 'd', 'c', 'v', 'a', 'ArrowLeft', 'ArrowUp', 'ArrowRight', 'ArrowDown', ' ', '[', ']', '+', '-']);
    const ctrl = rnd(3) === 0, shift = rnd(4) === 0;
    const target = document.activeElement || document.body;
    const init = { key, bubbles: true, cancelable: true, ctrlKey: ctrl, shiftKey: shift };
    target.dispatchEvent(new KeyboardEvent('keydown', init));
    target.dispatchEvent(new KeyboardEvent('keyup', init));
    done = 'key ' + (ctrl ? 'Ctrl+' : '') + (shift ? 'Shift+' : '') + key;
  } else if (roll < 94) {
    const el = pick(find('canvas,.stage,aside,ul,.layers'));
    if (el) { fire(el, 'wheel', at(el, 0.5, 0.5), { deltaY: rnd(2) ? 120 : -120, ctrlKey: rnd(3) === 0 }); done = 'wheel ' + name(el); }
  } else {
    const el = pick(find('canvas,li,.stage,button'));
    if (el) { fire(el, 'contextmenu', at(el, 0.5, 0.5), { button: 2 }); done = 'right-click ' + name(el); }
  }
  const errors = m.errors.splice(0);
  return JSON.stringify({ done, errors });
})()
