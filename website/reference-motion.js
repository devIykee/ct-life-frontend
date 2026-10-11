let currentDisposer = null;

/** Scroll-driven enhancements; the document's unmodified styles are the static view. */
export function initReferenceMotion() {
  if (currentDisposer) return currentDisposer;

  const body = document.body;
  if (!body) return () => {};
  const root = document.documentElement;

  const opening = document.querySelector('.opening');
  const artwork = opening?.querySelector('.opening-art img');
  const rail = opening?.querySelector('.identity-rail .rail-word');
  const chapter = document.querySelector('.chapter#life');
  const chapterTitle = chapter?.querySelector('.chapter-title');
  const firstLine = chapter?.querySelector('[data-line="1"]');
  const secondLine = chapter?.querySelector('[data-line="2"]');
  const note = chapter?.querySelector('.chapter-note');
  const worldFigure = document.querySelector('.world-scene figure');
  const worldImage = worldFigure?.querySelector('img');
  const toggle = document.querySelector('button[data-motion-toggle]');
  const label = toggle?.querySelector('[data-motion-label]') || toggle;
  const preference = window.matchMedia('(prefers-reduced-motion: reduce)');
  const originalMode = body.getAttribute('data-motion');
  const originalRootMode = root.getAttribute('data-motion');
  const originalLabel = label?.textContent;
  const originalPressed = toggle?.getAttribute('aria-pressed');
  const originalDisabled = toggle?.disabled;
  const originalTitle = toggle?.getAttribute('title');
  const savedStyles = [];

  for (const [element, properties] of [
    [artwork, ['translate']], [rail, ['translate']],
    [firstLine, ['translate', 'scale']], [secondLine, ['translate', 'scale']],
    [note, ['opacity']], [chapter, ['--chapter-progress']],
    [worldFigure, ['clip-path', 'overflow']], [worldImage, ['scale']],
  ]) {
    if (!element) continue;
    for (const property of properties) {
      savedStyles.push([element, property, element.style.getPropertyValue(property),
        element.style.getPropertyPriority(property)]);
    }
  }
  const worldStyles = savedStyles.filter(([element]) => element === worldFigure || element === worldImage);

  let paused = (originalMode || originalRootMode) === 'reduced' && !preference.matches;
  let disposed = false;
  let frame = null;
  // The first synchronous render also handles a restored scroll position.
  let openingVisible = true;
  let chapterVisible = true;
  let worldVisible = true;
  const clamp = value => Math.min(1, Math.max(0, value));
  const isReduced = () => paused || preference.matches;

  function restoreStyles(styles) {
    for (const [element, property, value, priority] of styles) {
      if (value) element.style.setProperty(property, value, priority);
      else element.style.removeProperty(property);
    }
  }

  function setStatic() {
    if (artwork) artwork.style.translate = '0px 0px';
    if (rail) rail.style.translate = '0px 0px';
    for (const line of [firstLine, secondLine]) {
      if (!line) continue;
      line.style.translate = '0px 0px';
      line.style.scale = '1';
    }
    if (note) note.style.opacity = '1';
    chapter?.style.setProperty('--chapter-progress', '1');
    restoreStyles(worldStyles);
  }

  function render() {
    frame = null;
    if (disposed || document.hidden || isReduced()) return;
    const viewport = window.innerHeight;
    const mobile = window.innerWidth <= 700;
    // Read geometry together before writing any presentation properties.
    const openingRect = openingVisible ? opening?.getBoundingClientRect() : null;
    const chapterRect = chapterVisible ? chapter?.getBoundingClientRect() : null;
    const titleRect = chapterVisible ? chapterTitle?.getBoundingClientRect() : null;
    const worldRect = worldVisible ? worldFigure?.getBoundingClientRect() : null;

    if (openingRect) {
      const progress = clamp(-openingRect.top / Math.max(1, openingRect.height));
      if (artwork) artwork.style.translate = `0px ${(progress * (mobile ? 30 : 45)).toFixed(2)}px`;
      if (rail) rail.style.translate = `0px ${(-progress * 30).toFixed(2)}px`;
    }
    if (chapterRect) {
      // Settle on entry, by the title's center reaching the viewport's center.
      // Measure the untransformed title wrapper so its children cannot feed back.
      const titleCenter = titleRect ? titleRect.top + titleRect.height / 2 : chapterRect.top;
      const progress = clamp((viewport * 1.12 - titleCenter) / Math.max(1, viewport * 0.62));
      const remaining = 1 - progress * progress * (3 - 2 * progress);
      const distance = (mobile ? 18 : 40) * remaining;
      chapter.style.setProperty('--chapter-progress', progress.toFixed(4));
      if (firstLine) firstLine.style.translate = `${distance.toFixed(2)}px 0px`;
      if (secondLine) secondLine.style.translate = `${(-distance).toFixed(2)}px 0px`;
      for (const line of [firstLine, secondLine]) {
        if (line) line.style.scale = (1 + remaining * (mobile ? 0.01 : 0.02)).toFixed(4);
      }
      if (note) {
        const fade = clamp((progress - 0.2) / 0.65);
        note.style.opacity = (fade * fade * (3 - 2 * fade)).toFixed(4);
      }
    }
    if (mobile) restoreStyles(worldStyles);
    else if (worldRect) {
      const progress = clamp((viewport - worldRect.top) / Math.max(1, (viewport + worldRect.height) / 2));
      const remaining = 1 - progress * progress * (3 - 2 * progress);
      worldFigure.style.overflow = 'hidden';
      worldFigure.style.clipPath = `inset(0px ${(24 * remaining).toFixed(2)}px)`;
      if (worldImage) worldImage.style.scale = (1 + remaining * 0.04).toFixed(4);
    }
  }

  function schedule() {
    if (!disposed && !document.hidden && !isReduced() && frame === null) {
      frame = window.requestAnimationFrame(render);
    }
  }

  function cancelFrame() {
    if (frame !== null) window.cancelAnimationFrame(frame);
    frame = null;
  }

  function syncMode() {
    cancelFrame();
    const reduced = isReduced();
    body.dataset.motion = reduced ? 'reduced' : 'full';
    root.dataset.motion = body.dataset.motion;
    if (toggle) {
      toggle.setAttribute('aria-pressed', String(reduced));
      toggle.disabled = preference.matches;
      if (preference.matches) toggle.title = 'Reduced motion is enabled in your system settings.';
      else if (originalTitle === null) toggle.removeAttribute('title');
      else toggle.setAttribute('title', originalTitle);
    }
    if (label) label.textContent = preference.matches ? 'Reduced motion' : reduced ? 'Resume motion' : 'Pause motion';
    if (reduced) setStatic();
    else schedule();
  }

  function onToggle() {
    if (preference.matches) return;
    paused = !paused;
    syncMode();
  }

  function onVisibility() {
    if (document.hidden) cancelFrame();
    else schedule();
  }

  const observer = 'IntersectionObserver' in window ? new IntersectionObserver(entries => {
    for (const entry of entries) {
      if (entry.target === opening) openingVisible = entry.isIntersecting;
      if (entry.target === chapter) chapterVisible = entry.isIntersecting;
      if (entry.target === worldFigure) worldVisible = entry.isIntersecting;
    }
    schedule();
  }) : null;
  if (opening) observer?.observe(opening);
  if (chapter) observer?.observe(chapter);
  if (worldFigure) observer?.observe(worldFigure);

  window.addEventListener('scroll', schedule, { passive: true });
  window.addEventListener('resize', schedule, { passive: true });
  document.addEventListener('visibilitychange', onVisibility);
  preference.addEventListener('change', syncMode);
  toggle?.addEventListener('click', onToggle);
  syncMode();

  currentDisposer = () => {
    if (disposed) return;
    disposed = true;
    cancelFrame();
    observer?.disconnect();
    window.removeEventListener('scroll', schedule);
    window.removeEventListener('resize', schedule);
    document.removeEventListener('visibilitychange', onVisibility);
    preference.removeEventListener('change', syncMode);
    toggle?.removeEventListener('click', onToggle);
    restoreStyles(savedStyles);
    if (originalMode === null) body.removeAttribute('data-motion');
    else body.setAttribute('data-motion', originalMode);
    if (originalRootMode === null) root.removeAttribute('data-motion');
    else root.setAttribute('data-motion', originalRootMode);
    if (toggle) {
      if (originalPressed === null) toggle.removeAttribute('aria-pressed');
      else toggle.setAttribute('aria-pressed', originalPressed);
      toggle.disabled = originalDisabled;
      if (originalTitle === null) toggle.removeAttribute('title');
      else toggle.setAttribute('title', originalTitle);
    }
    if (label) label.textContent = originalLabel;
    currentDisposer = null;
  };
  return currentDisposer;
}
