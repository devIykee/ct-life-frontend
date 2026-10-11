const captures = Object.freeze([
  {
    file: 'proof-timeline.webp', title: 'Timeline Square', build: 'V7 · 7 October 2026',
    detail: 'Timeline Square, with the in-game phone and movement controls.',
    alt: 'Seeker development capture of a character in Timeline Square. Movement and phone controls surround the city. The image is marked Visual Review, Sample Data and Development Build.',
  },
  {
    file: 'proof-clip-cut.webp', title: 'Clip Cut', build: 'V7 · 7 October 2026',
    detail: 'A local practice round: trim the setup and payoff into one short clip. No posting or payout.',
    alt: 'Seeker development capture of Clip Cut, with eight storyboard frames, trim controls and a preview. On-screen labels state Local Practice, No Posting or Payout, and Visual Review, Sample Data.',
  },
  {
    file: 'proof-cafe.webp', title: 'Terminal Café', build: 'V6 · 7 October 2026',
    detail: 'Inside the café, with tables, terminals and a place to step away from the timeline.',
    alt: 'Seeker development capture of a character in Terminal Café among tables and computer desks. The image is marked Visual Review, Sample Data and Development Build.',
  },
]);

let activeGallery;

/** Mount one native modal. No screenshot request is made until the viewer opens. */
export function initPreviewGallery({ triggerSelector = '[data-preview-gallery]' } = {}) {
  if (activeGallery) return activeGallery;
  const listeners = new AbortController();
  const on = (element, type, callback) => element.addEventListener(type, callback, { signal: listeners.signal });
  const make = (tag, className, text) => {
    const element = document.createElement(tag);
    element.className = className;
    if (text) element.textContent = text;
    return element;
  };
  const assetURL = file => new URL(`./assets/campaign-20261008/${file}`, import.meta.url).href;
  const dialog = make('dialog', 'preview-dialog');
  dialog.id = 'current-build-preview';
  dialog.setAttribute('aria-labelledby', 'preview-title');
  dialog.setAttribute('aria-describedby', 'preview-disclosure');
  const header = make('header', 'preview-header');
  const heading = make('div', 'preview-heading');
  heading.append(make('p', 'preview-eyebrow', 'FROM THE ACTUAL BUILD'));
  const title = make('h2', 'preview-title', 'Inside CT Life.');
  title.id = 'preview-title';
  heading.append(title);
  const closeButton = make('button', 'preview-close', 'Close ×');
  closeButton.type = 'button';
  closeButton.autofocus = true;
  closeButton.setAttribute('aria-label', 'Close current build preview');
  header.append(heading, closeButton);
  const disclosure = make('p', 'preview-disclosure', 'Captured on Solana Seeker in local development preview mode, using sample data. These images do not show live multiplayer.');
  disclosure.id = 'preview-disclosure';
  const figure = make('figure', 'preview-figure');
  const frame = make('div', 'preview-frame');
  const image = make('img', 'preview-image');
  image.width = 2670;
  image.height = 1200;
  image.decoding = 'async';
  const imageStatus = make('p', 'preview-image-status');
  imageStatus.setAttribute('role', 'status');
  const retry = make('button', 'preview-retry', 'Try image again');
  retry.type = 'button';
  retry.hidden = true;
  const loadMessage = make('div', 'preview-load-message');
  loadMessage.append(imageStatus, retry);
  frame.append(image, loadMessage);
  const caption = make('figcaption', 'preview-caption');
  const captionTop = make('div', 'preview-caption-top');
  const name = make('h3', 'preview-name');
  const build = make('p', 'preview-build');
  captionTop.append(name, build);
  const detail = make('p', 'preview-detail');
  caption.append(captionTop, detail);
  figure.append(frame, caption);
  const footer = make('div', 'preview-footer');
  const fullSize = make('a', 'preview-full-size', 'View full size ↗');
  fullSize.target = '_blank';
  fullSize.rel = 'noopener noreferrer';
  const controls = make('div', 'preview-controls');
  controls.setAttribute('role', 'group');
  controls.setAttribute('aria-label', 'Preview images');
  const previous = make('button', 'preview-arrow', '←');
  previous.type = 'button';
  previous.setAttribute('aria-label', 'Previous image');
  const counter = make('span', 'preview-counter');
  counter.setAttribute('aria-hidden', 'true');
  const next = make('button', 'preview-arrow', '→');
  next.type = 'button';
  next.setAttribute('aria-label', 'Next image');
  controls.append(previous, counter, next);
  footer.append(fullSize, controls);
  const announcement = make('p', 'preview-sr-only');
  announcement.setAttribute('role', 'status');
  announcement.setAttribute('aria-atomic', 'true');
  dialog.append(header, disclosure, figure, footer, announcement);
  document.body.append(dialog);

  let index = 0;
  let loadVersion = 0;
  let lastTrigger;
  let destroyed = false;
  let outsidePointer = false;
  let ownsScrollLock = false;
  let pendingImage;

  function select(value, retryLoad = false) {
    index = (value + captures.length) % captures.length;
    const capture = captures[index];
    const url = assetURL(capture.file);
    const version = ++loadVersion;
    if (pendingImage) pendingImage.onload = pendingImage.onerror = null;
    name.textContent = capture.title;
    build.textContent = capture.build;
    detail.textContent = capture.detail;
    counter.textContent = `${index + 1} / ${captures.length}`;
    announcement.textContent = `Image ${index + 1} of ${captures.length}: ${capture.title}. ${capture.build}.`;
    fullSize.href = url;
    fullSize.setAttribute('aria-label', `View ${capture.title} at full size, opens a new tab`);
    frame.setAttribute('aria-busy', 'true');
    image.hidden = true;
    if (document.activeElement === retry) closeButton.focus({ preventScroll: true });
    retry.hidden = true;
    loadMessage.hidden = false;
    imageStatus.textContent = 'Loading capture…';
    const candidate = new Image();
    pendingImage = candidate;
    const fail = () => {
      if (destroyed || version !== loadVersion) return;
      imageStatus.textContent = 'This capture could not load.';
      retry.hidden = false;
      frame.setAttribute('aria-busy', 'false');
      pendingImage = null;
    };
    candidate.onload = async () => {
      if (destroyed || version !== loadVersion) return;
      image.alt = capture.alt;
      image.src = candidate.src;
      // The preload's load event does not establish this displayed element's
      // decoding readiness. Keep feedback visible through its own decode.
      try {
        await image.decode();
      } catch {
        fail();
        return;
      }
      if (destroyed || version !== loadVersion) return;
      image.hidden = false;
      loadMessage.hidden = true;
      frame.setAttribute('aria-busy', 'false');
      pendingImage = null;
    };
    candidate.onerror = fail;
    candidate.src = retryLoad ? `${url}?retry=${Date.now()}` : url;
  }

  function restorePage() {
    if (ownsScrollLock) document.body.classList.remove('preview-open');
    ownsScrollLock = false;
    outsidePointer = false;
    if (lastTrigger?.isConnected && !document.querySelector('dialog[open]')) lastTrigger.focus({ preventScroll: true });
  }
  function close() {
    if (dialog.open) dialog.close();
    restorePage();
  }
  function open(trigger = document.activeElement) {
    if (destroyed || dialog.open) return;
    if (typeof dialog.showModal !== 'function') {
      window.location.assign(assetURL(captures[0].file));
      return;
    }
    lastTrigger = trigger;
    dialog.showModal();
    ownsScrollLock = !document.body.classList.contains('preview-open');
    document.body.classList.add('preview-open');
    select(0);
    closeButton.focus({ preventScroll: true });
  }
  function isOutside(event) {
    if (event.target !== dialog) return false;
    const rect = dialog.getBoundingClientRect();
    return event.clientX < rect.left || event.clientX > rect.right || event.clientY < rect.top || event.clientY > rect.bottom;
  }
  on(document, 'click', event => {
    const trigger = event.target instanceof Element ? event.target.closest(triggerSelector) : null;
    if (!trigger) return;
    event.preventDefault();
    open(trigger);
  });
  on(closeButton, 'click', close);
  on(previous, 'click', () => select(index - 1));
  on(next, 'click', () => select(index + 1));
  on(retry, 'click', () => select(index, true));
  on(dialog, 'close', () => { if (!dialog.open) restorePage(); });
  on(dialog, 'cancel', event => { event.preventDefault(); close(); });
  on(dialog, 'pointerdown', event => { outsidePointer = isOutside(event); });
  on(dialog, 'pointercancel', () => { outsidePointer = false; });
  on(dialog, 'click', event => {
    if (outsidePointer && isOutside(event)) close();
    outsidePointer = false;
  });
  on(dialog, 'keydown', event => {
    if (event.key === 'Escape') { event.stopPropagation(); return; }
    if (event.key === 'Tab') {
      const available = [closeButton, retry, fullSize, previous, next].filter(element => !element.hidden && element.getClientRects().length);
      const current = available.indexOf(document.activeElement);
      if (current === -1 || (!event.shiftKey && current === available.length - 1) || (event.shiftKey && current === 0)) {
        event.preventDefault();
        available[event.shiftKey ? available.length - 1 : 0].focus();
      }
      return;
    }
    if (event.altKey || event.ctrlKey || event.metaKey || event.shiftKey) return;
    const keyIndex = { ArrowLeft: index - 1, ArrowRight: index + 1, Home: 0, End: captures.length - 1 }[event.key];
    if (keyIndex === undefined) return;
    event.preventDefault();
    event.stopPropagation();
    select(keyIndex);
  });

  activeGallery = Object.freeze({
    open, close,
    destroy() {
      if (destroyed) return;
      destroyed = true;
      ++loadVersion;
      if (pendingImage) pendingImage.onload = pendingImage.onerror = null;
      close();
      listeners.abort();
      dialog.remove();
      activeGallery = undefined;
    },
  });
  return activeGallery;
}
