'use strict';
const $ = (selector, parent = document) => parent.querySelector(selector);
const $$ = (selector, parent = document) => [...parent.querySelectorAll(selector)];
const menu = $('.menu-toggle');
const nav = $('#main-nav');
const closeMenu = () => { if (!menu || !nav) return; menu.setAttribute('aria-expanded', 'false'); nav.classList.remove('is-open'); };
menu?.addEventListener('click', () => { const open = menu.getAttribute('aria-expanded') !== 'true'; menu.setAttribute('aria-expanded', String(open)); nav.classList.toggle('is-open', open); });
nav?.addEventListener('click', event => { if (event.target.closest('a')) closeMenu(); });
document.addEventListener('keydown', event => { if (event.key === 'Escape' && menu?.getAttribute('aria-expanded') === 'true') { closeMenu(); menu.focus(); } });
document.addEventListener('click', event => { if (menu && !event.target.closest('.site-header')) closeMenu(); });
const districts = {
  timeline: { number: '01', tone: 'cyan', icon: 'buildings', status: 'In development', kicker: 'THE CITY’S FRONT DOOR', name: 'Timeline Square', description: 'Start in the square. Walk the streets, find your bearings and open your in-game phone. Your first corner of CT City is taking shape.', detailLabel: 'Taking shape', detail: '3D streets · direct movement · map and phone' },
  cafe: { number: '02', tone: 'coral', icon: 'coins', status: 'Venue preview · markets planned', kicker: 'ALPHA WITH YOUR COFFEE', name: 'Terminal Café', description: 'A home for chart-watchers, late-night builders and the friend who swears they called it early. Fictional trading and markets that react to city drama are planned.', detailLabel: 'Planned activity', detail: 'Fictional memecoins · market events · trading terminals' },
  ore: { number: '03', tone: 'gold', icon: 'hexagon', status: 'Planned district', kicker: 'THE REFINERY', name: 'ORE Refinery', description: 'ORE gets a place of its own. Follow the board, build a miner identity and explore staking. These activities are planned for the Refinery.', detailLabel: 'Planned activity', detail: 'ORE board · staking · miner identity · ORE payments' },
  spaces: { number: '04', tone: 'purple', icon: 'broadcast', status: 'Venue preview · calls planned', kicker: 'UNMUTE YOURSELF', name: 'Spaces Studio', description: 'The place for hot takes, open mics and conversations that run a little too long. Hosted voice rooms, video and paid calls are planned.', detailLabel: 'Planned activity', detail: 'Hosted Spaces · voice and video · paid calls' }
};
function bindTabs(selector, select) {
  const tabs = $$(selector);
  const choose = tab => { tabs.forEach(item => { const active = item === tab; item.setAttribute('aria-selected', String(active)); item.tabIndex = active ? 0 : -1; }); select(tab); };
  tabs.forEach((tab, index) => {
    tab.addEventListener('click', () => choose(tab));
    tab.addEventListener('keydown', event => {
      let next;
      if (event.key === 'ArrowRight' || event.key === 'ArrowDown') next = (index + 1) % tabs.length;
      else if (event.key === 'ArrowLeft' || event.key === 'ArrowUp') next = (index + tabs.length - 1) % tabs.length;
      else if (event.key === 'Home') next = 0;
      else if (event.key === 'End') next = tabs.length - 1;
      if (next === undefined) return;
      event.preventDefault(); choose(tabs[next]); tabs[next].focus();
    });
  });
}
bindTabs('[data-district]', tab => {
  const d = districts[tab.dataset.district]; const panel = $('#district-panel');
  panel.setAttribute('aria-labelledby', tab.id); panel.dataset.tone = d.tone;
  $('.district-number').textContent = d.number;
  for (const [id, value] of Object.entries({ 'district-status': d.status, 'district-kicker': d.kicker, 'district-name': d.name, 'district-description': d.description, 'district-detail-label': d.detailLabel, 'district-detail': d.detail })) $(`#${id}`).textContent = value;
  $('#district-icon').src = `assets/icons/${d.icon}.png`;
});
const looks = {
  everyday: ['Everyday', 'Charcoal tee. Light denim. Blue sneakers. An everyday look for a not-so-everyday city.'],
  breeze: ['Breeze', 'A white top, a blue scarf and easy denim. A little fresh air for a city that’s always online.']
};
bindTabs('[data-look]', tab => {
  const [name, description] = looks[tab.dataset.look]; $('#look-name').textContent = name; $('#look-description').textContent = description; $('#look-panel').setAttribute('aria-labelledby', tab.id);
  $$('[data-character-card]').forEach(card => card.classList.toggle('is-selected', card.dataset.characterCard === tab.dataset.look));
});
const buildCopy = {
  now: [ ['City & identity', 'Explore a 3D city, choose your look and carry your world in an in-game phone.', 'In development'], ['Solana sign-in', 'Your Solana wallet is your way into CT Life. Wallet sign-in is being built into the Seeker experience.', 'In development'], ['Game Cash', 'Cash is an in-game balance for the simulation. It is separate from your wallet assets and has no cash-out route.', 'Game economy'] ],
  next: [ ['Money & markets', 'Fictional markets, an ORE board and staking, plus wallet payments with USDC, SOL, SKR and ORE.', 'In the plan'], ['Voice & city life', 'Hosted Spaces, messaging, voice and video calls, richer homes, relationships and a broader wardrobe. Paid calls are part of the plan.', 'In the plan'], ['Projects & competition', 'Player-founded projects, city advertising, businesses, leagues and seasons. More ways to make your name in CT City.', 'In the plan'] ]
};
bindTabs('[data-build]', tab => {
  const panel = $('#build-panel'); panel.setAttribute('aria-labelledby', tab.id); panel.replaceChildren(...buildCopy[tab.dataset.build].map(([title, text, status], index) => {
    const article = document.createElement('article'), n = document.createElement('span'), copy = document.createElement('div'), h = document.createElement('h3'), p = document.createElement('p'), badge = document.createElement('span');
    n.className = 'build-index'; n.textContent = `0${index + 1}`; h.textContent = title; p.textContent = text; badge.className = 'status-label'; badge.textContent = status; copy.append(h, p); article.append(n, copy, badge); return article;
  }));
});
const viewer = $('#image-viewer');
const screenshotTitles = { dashboard: 'The lobby', city: 'Timeline Square', phone: 'The in-game phone' };
let lastTrigger;
$$('[data-screenshot]').forEach(button => button.addEventListener('click', () => {
  lastTrigger = button; const name = button.dataset.screenshot;
  $('#viewer-title').textContent = screenshotTitles[name]; $('#viewer-image').src = `assets/game-${name}.png`; $('#viewer-image').alt = $('img', button).alt;
  viewer.showModal(); document.body.style.overflow = 'hidden';
}));
$('.viewer-close')?.addEventListener('click', () => viewer.close());
viewer?.addEventListener('click', event => { if (event.target !== viewer) return; const r = viewer.getBoundingClientRect(); if (event.clientX < r.left || event.clientX > r.right || event.clientY < r.top || event.clientY > r.bottom) viewer.close(); });
viewer?.addEventListener('close', () => { document.body.style.overflow = ''; lastTrigger?.focus(); });
const motion = $('.motion-toggle');
const reducedMotion = matchMedia('(prefers-reduced-motion: reduce)');
let motionPaused = reducedMotion.matches;
const refreshMotion = () => { document.body.classList.toggle('motion-paused', motionPaused); if (motion) { motion.setAttribute('aria-pressed', String(motionPaused)); motion.textContent = motionPaused ? 'Motion paused' : 'Pause motion'; } };
refreshMotion();
motion?.addEventListener('click', () => { motionPaused = !motionPaused; refreshMotion(); });
reducedMotion.addEventListener('change', event => { if (event.matches) { motionPaused = true; refreshMotion(); } });
