// The unenhanced page stays visible. This module owns only temporary animation
// effects and the documented custom properties, never the artwork's transform.
let disposeCurrent = null;

export function initCampaignMotion() {
  if (typeof window === 'undefined' || typeof document === 'undefined') return () => {};
  if (disposeCurrent) return disposeCurrent;

  const root = document.documentElement;
  const artwork = document.querySelector('.hero-artwork');
  const city = document.querySelector('.city-frame');
  const buttons = [...document.querySelectorAll('[data-motion-pause]')];
  const reveals = new Set(document.querySelectorAll('[data-reveal]'));
  const seen = new WeakSet();
  const visible = new Set();
  const animations = new Map();
  const reduced = window.matchMedia('(prefers-reduced-motion: reduce)');
  const desktopPointer = window.matchMedia('(min-width: 900px) and (hover: hover) and (pointer: fine)');
  const removers = [];
  const previousMode = root.getAttribute('data-motion');
  const buttonState = buttons.map(button => ({
    button,
    pressed: button.getAttribute('aria-pressed'),
    disabled: button.getAttribute('aria-disabled'),
    label: button.querySelector('[data-motion-label]'),
    text: button.querySelector('[data-motion-label]')?.textContent,
  }));
  let paused = false;
  let disposed = false;
  let frame = 0;
  let pointer = null;
  let observer = null;

  const clamp = (value, min, max) => Math.min(max, Math.max(min, value));
  const active = () => !disposed && !paused && !reduced.matches && !document.hidden;
  const listen = (target, type, handler, options) => {
    target.addEventListener(type, handler, options);
    removers.push(() => target.removeEventListener(type, handler, options));
  };
  const mediaListen = (query, handler) => {
    if (query.addEventListener) listen(query, 'change', handler);
    else {
      query.addListener(handler);
      removers.push(() => query.removeListener(handler));
    }
  };
  const set = (element, name, value) => {
    if (element && element.style.getPropertyValue(name) !== value) element.style.setProperty(name, value);
  };
  const resetArtwork = () => {
    set(artwork, '--pointer-x', '0');
    set(artwork, '--pointer-y', '0');
    set(artwork, '--scroll-y', '0px');
  };
  const reset = () => {
    pointer = null;
    resetArtwork();
    set(city, '--aperture', '1');
  };
  const cancelReveal = element => {
    const animation = animations.get(element);
    if (!animation) return;
    animations.delete(element);
    animation.cancel(); // Removing the effect exposes the visible underlying CSS.
  };
  const cancelReveals = () => [...animations.keys()].forEach(cancelReveal);

  function updateFrame() {
    frame = 0;
    if (!active()) return;
    const height = window.innerHeight;
    // Read geometry together, then write. No polling or self-scheduled frame loop.
    const heroBounds = artwork?.getBoundingClientRect();
    const cityBounds = city?.getBoundingClientRect();
    const heroOnscreen = heroBounds && heroBounds.bottom > 0 && heroBounds.top < height;
    const cityOnscreen = cityBounds && cityBounds.bottom > 0 && cityBounds.top < height;
    if (heroOnscreen) {
      const usePointer = pointer && desktopPointer.matches && heroBounds.width > 0 && heroBounds.height > 0;
      const x = usePointer ? clamp(((pointer.x - heroBounds.left) / heroBounds.width) * 2 - 1, -1, 1) : 0;
      const y = usePointer ? clamp(((pointer.y - heroBounds.top) / heroBounds.height) * 2 - 1, -1, 1) : 0;
      set(artwork, '--pointer-x', x.toFixed(3));
      set(artwork, '--pointer-y', y.toFixed(3));
      // Coarse/narrow devices keep the artwork stable while scrolling.
      set(artwork, '--scroll-y', desktopPointer.matches ? `${clamp(-heroBounds.top * 0.035, -20, 20).toFixed(2)}px` : '0px');
    } else resetArtwork();
    if (cityOnscreen) {
      const progress = clamp((height * 0.9 - cityBounds.top) / Math.max(1, height * 0.55), 0, 1);
      set(city, '--aperture', desktopPointer.matches ? progress.toFixed(4) : '1');
    }
  }

  function schedule() {
    if (active() && !frame) frame = window.requestAnimationFrame(updateFrame);
  }

  function reveal(element, index) {
    if (seen.has(element)) return;
    seen.add(element);
    if (!active() || typeof element.animate !== 'function' || element.contains(document.activeElement)) return;
    try {
      const keyframes = window.CSS?.supports('translate', '0 1px')
        ? [{ opacity: 0, translate: '0 18px' }, { opacity: 1, translate: '0 0' }]
        : [{ opacity: 0 }, { opacity: 1 }];
      const animation = element.animate(keyframes, {
        duration: 520,
        delay: Math.min(index * 45, 180),
        easing: 'cubic-bezier(0.22, 1, 0.36, 1)',
        fill: 'backwards',
        iterations: 1,
      });
      animations.set(element, animation);
      animation.onfinish = () => {
        animations.delete(element);
        animation.cancel();
      };
    } catch {
      // Unsupported animation must not hide content or break navigation.
    }
  }

  function syncMode() {
    const mode = reduced.matches ? 'reduced' : paused || document.hidden ? 'paused' : 'full';
    root.dataset.motion = mode;
    buttons.forEach(button => {
      button.setAttribute('aria-pressed', String(paused || reduced.matches));
      button.setAttribute('aria-disabled', String(reduced.matches));
      const label = button.querySelector('[data-motion-label]');
      if (label) label.textContent = reduced.matches ? 'Reduced motion' : paused ? 'Resume motion' : 'Pause motion';
    });
    if (!active()) {
      if (frame) window.cancelAnimationFrame(frame);
      frame = 0;
      cancelReveals();
      reset();
      // Already-read content must not replay an entrance on resume.
      visible.forEach(element => { if (reveals.has(element)) seen.add(element); });
    } else schedule();
  }

  reset();
  if ('IntersectionObserver' in window) {
    observer = new IntersectionObserver(entries => {
      if (disposed) return;
      let stagger = 0;
      for (const entry of entries) {
        if (entry.isIntersecting) {
          visible.add(entry.target);
          if (reveals.has(entry.target)) reveal(entry.target, stagger++);
        } else {
          visible.delete(entry.target);
          cancelReveal(entry.target);
        }
      }
      schedule();
    }, { threshold: 0, rootMargin: '0px 0px -6% 0px' });
    new Set([...reveals, artwork, city].filter(Boolean)).forEach(element => observer.observe(element));
  }
  // Without IntersectionObserver, entrances remain static and visible.
  listen(window, 'scroll', schedule, { passive: true });
  listen(window, 'resize', () => { pointer = null; schedule(); }, { passive: true });
  listen(document, 'visibilitychange', syncMode);
  listen(document, 'focusin', event => {
    for (const element of animations.keys()) if (element.contains(event.target)) cancelReveal(element);
  });
  if (artwork) {
    listen(artwork, 'pointermove', event => {
      if (!active() || !desktopPointer.matches || event.pointerType === 'touch') return;
      pointer = { x: event.clientX, y: event.clientY };
      schedule();
    }, { passive: true });
    const leave = () => { pointer = null; schedule(); };
    listen(artwork, 'pointerleave', leave, { passive: true });
    listen(artwork, 'pointercancel', leave, { passive: true });
  }
  mediaListen(reduced, syncMode);
  mediaListen(desktopPointer, () => { pointer = null; schedule(); });
  buttons.forEach(button => listen(button, 'click', () => {
    if (reduced.matches) return; // The OS preference always takes precedence.
    paused = !paused;
    syncMode();
  }));
  syncMode();

  disposeCurrent = () => {
    if (disposed) return;
    disposed = true;
    if (frame) window.cancelAnimationFrame(frame);
    observer?.disconnect();
    removers.forEach(remove => remove());
    cancelReveals();
    reset();
    if (previousMode === null) root.removeAttribute('data-motion');
    else root.setAttribute('data-motion', previousMode);
    const restore = (button, attribute, value) => {
      if (value === null) button.removeAttribute(attribute);
      else button.setAttribute(attribute, value);
    };
    buttonState.forEach(({ button, pressed, disabled, label, text }) => {
      restore(button, 'aria-pressed', pressed);
      restore(button, 'aria-disabled', disabled);
      if (label) label.textContent = text;
    });
    disposeCurrent = null;
  };
  return disposeCurrent;
}
