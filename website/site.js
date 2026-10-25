import { mountIslandMap } from "./map.js?v=3";

// Set this to the store/APK link on launch day; every "Get the game" follows it.
const DOWNLOAD_URL = "";

const $ = (s, r = document) => r.querySelector(s);
const $$ = (s, r = document) => [...r.querySelectorAll(s)];
const store = {
  get(k) {
    try { return localStorage.getItem(k); } catch { return null; }
  },
  set(k, v) {
    try { localStorage.setItem(k, v); } catch {}
  },
};
const reduced = matchMedia("(prefers-reduced-motion: reduce)").matches;
const root = document.documentElement;
const isPaused = () => root.dataset.motion === "paused";
const rand = (n) => crypto.getRandomValues(new Uint32Array(1))[0] / 2 ** 32 * n;
const clamp = (v, a, b) => Math.min(b, Math.max(a, v));

/* ---------------------------------------------------------------- motion toggle */
if (store.get("ctlife-motion") === "paused" || (reduced && store.get("ctlife-motion") !== "full")) {
  root.dataset.motion = "paused";
}
const motionBtn = $("[data-motion-toggle]");
const heroVideo = $("[data-hero-video]");
const syncMotion = () => {
  const p = isPaused();
  motionBtn.setAttribute("aria-pressed", String(p));
  $(".sr", motionBtn).textContent = p ? "Play motion" : "Pause motion";
  if (p) heroVideo.pause();
  else heroVideo.play().catch(() => {});
  $$("video[data-bg]").forEach((v) => (p ? v.pause() : v.dataset.visible === "1" && v.play().catch(() => {})));
  syncReelVideo();
};
motionBtn.addEventListener("click", () => {
  root.dataset.motion = isPaused() ? "full" : "paused";
  store.set("ctlife-motion", root.dataset.motion);
  syncMotion();
});

/* ---------------------------------------------------------------- boot (tied to the reel being ready) */
const boot = $(".boot");
const bootStart = performance.now();
const setBoot = (p) => boot.style.setProperty("--p", `${Math.round(p * 100)}%`);
let booted = false;
const finishBoot = () => {
  if (booted) return;
  booted = true;
  const wait = Math.max(0, 900 - (performance.now() - bootStart));
  setTimeout(() => {
    setBoot(1);
    setTimeout(() => boot.classList.add("is-done"), 180);
    try { sessionStorage.setItem("ctlife-booted", "1"); } catch {}
  }, wait);
};
setBoot(0.25);
heroVideo.addEventListener("loadeddata", () => {
  setBoot(0.75);
  document.fonts.ready.then(finishBoot);
}, { once: true });
setTimeout(finishBoot, 2600); // never hold the page hostage

/* ---------------------------------------------------------------- hero */
if (isPaused()) heroVideo.removeAttribute("autoplay");
heroVideo.addEventListener("canplay", () => !isPaused() && heroVideo.play().catch(() => {}), { once: true });

const localTime = $("[data-local-time]");
const tickClock = () => {
  const d = new Date();
  localTime.textContent = `LOCAL ${String(d.getHours()).padStart(2, "0")}:${String(d.getMinutes()).padStart(2, "0")}`;
};
tickClock();
setInterval(tickClock, 10000);

// Market Mood: like the game, it follows SOL's real 24h change
const mood = $("[data-mood]");
fetch("https://api.coingecko.com/api/v3/simple/price?ids=solana&vs_currencies=usd&include_24hr_change=true")
  .then((r) => (r.ok ? r.json() : Promise.reject()))
  .then((d) => {
    const ch = d?.solana?.usd_24h_change;
    if (typeof ch !== "number") return;
    $("[data-mood-value]").textContent = `${ch >= 0 ? "+" : ""}${ch.toFixed(1)}%`;
    $("[data-mood-label]").textContent = ch >= 3 ? "SOL 24H / WE'RE SO BACK" : ch <= -3 ? "SOL 24H / IT'S SO OVER" : "SOL 24H / CHOP";
    mood.classList.toggle("down", ch < 0);
    mood.hidden = false;
  })
  .catch(() => {});

const ticker = $("[data-ticker]");
ticker.innerHTML += ticker.innerHTML; // seamless loop

/* ---------------------------------------------------------------- reveals */
$$("[data-split]").forEach((el) => {
  let i = 0;
  const walk = (node) => {
    [...node.childNodes].forEach((n) => {
      if (n.nodeType === 3) {
        const frag = document.createDocumentFragment();
        n.textContent.split(/(\s+)/).forEach((part) => {
          if (!part) return;
          if (/^\s+$/.test(part)) return frag.append(part);
          const w = document.createElement("span");
          w.className = "w";
          const inner = document.createElement("span");
          inner.textContent = part;
          inner.style.setProperty("--i", i++);
          w.append(inner);
          frag.append(w);
        });
        n.replaceWith(frag);
      } else if (n.nodeType === 1 && n.tagName !== "BR") walk(n);
    });
  };
  walk(el);
});
$$("[data-reveal-group]").forEach((g) => [...g.children].forEach((c, i) => c.style.setProperty("--i", i)));
const revealIO = new IntersectionObserver(
  (es) => es.forEach((e) => e.isIntersecting && (e.target.classList.add("is-in"), revealIO.unobserve(e.target))),
  { rootMargin: "0px 0px -12% 0px" }
);
$$("[data-reveal], [data-reveal-group], [data-split], [data-wipe]").forEach((el) => revealIO.observe(el));

/* ---------------------------------------------------------------- background videos */
const bgIO = new IntersectionObserver((es) =>
  es.forEach((e) => {
    const v = e.target;
    v.dataset.visible = e.isIntersecting ? "1" : "0";
    if (e.isIntersecting && !isPaused()) v.play().catch(() => {});
    else v.pause();
  }), { threshold: 0.3 });
$$("[data-autoplay] video").forEach((v) => {
  v.dataset.bg = "";
  bgIO.observe(v);
});

/* ---------------------------------------------------------------- inside: pinned reel */
const reel = $("[data-reel]");
const slides = $$(".reel-slide", reel);
const reelCards = $$(".reel-card", reel);
const dots = $$(".reel-dots li", reel);
const reelIndex = $("[data-reel-index]");
const track = $("[data-reel-track]");
reel.style.setProperty("--n", slides.length);
let ri = -1;
let reelVisible = false;
function syncReelVideo() {
  slides.forEach((s, i) => {
    const v = $("video", s);
    if (i === ri && reelVisible && !isPaused()) {
      if (v.preload === "none") v.preload = "auto";
      v.play().catch(() => {});
    } else v.pause();
    // warm the next clip so the slide never arrives empty
    if (i === ri + 1 && reelVisible) v.preload = "auto";
  });
}
const setReel = (i) => {
  if (i === ri) return;
  ri = i;
  track.style.setProperty("--ri", i);
  slides.forEach((s, k) => s.classList.toggle("on", k === i));
  reelCards.forEach((c, k) => c.classList.toggle("on", k === i));
  dots.forEach((d, k) => d.classList.toggle("on", k === i));
  reelIndex.textContent = String(i + 1).padStart(2, "0");
  syncReelVideo();
};
new IntersectionObserver(([e]) => {
  reelVisible = e.isIntersecting;
  syncReelVideo();
}).observe(reel);
setReel(0);

/* ---------------------------------------------------------------- scroll-linked motion */
const hero = $("[data-hero]");
const parallax = $$("[data-parallax]");
const drift = $$("[data-drift]");
let ticking = false;
const onScroll = () => {
  ticking = false;
  const vh = innerHeight;

  // hero recedes as you leave it
  const hr = hero.getBoundingClientRect();
  hero.style.setProperty("--exit", clamp(-hr.top / hr.height, 0, 1).toFixed(3));

  // reel: one slide per stretch of scroll inside the pinned section
  const rr = reel.getBoundingClientRect();
  const span = rr.height - vh;
  const p = clamp(-rr.top / span, 0, 0.999);
  setReel(Math.floor(p * slides.length));

  if (isPaused()) return;
  for (const el of parallax) {
    const r = el.parentElement.getBoundingClientRect();
    if (r.bottom < -100 || r.top > vh + 100) continue;
    const off = (r.top + r.height / 2 - vh / 2) / vh;
    el.style.setProperty("--py", `${(off * -9).toFixed(2)}%`);
  }
  for (const el of drift) {
    const r = el.getBoundingClientRect();
    if (r.bottom < 0 || r.top > vh) continue;
    el.style.setProperty("--dx", `${((r.top / vh) * 90 - 30).toFixed(1)}px`);
  }
};
addEventListener("scroll", () => {
  if (!ticking) {
    ticking = true;
    requestAnimationFrame(onScroll);
  }
}, { passive: true });
addEventListener("resize", onScroll);
onScroll();

/* ---------------------------------------------------------------- place cards */
const cards = $("[data-cards]");
const hoverCapable = matchMedia("(hover: hover) and (pointer: fine)").matches;
const ensureVideo = (card) => {
  let v = $("video", card);
  if (!v) {
    v = document.createElement("video");
    v.muted = true;
    v.loop = true;
    v.playsInline = true;
    v.preload = "auto";
    v.src = `assets/v2/clips/${card.dataset.clip}.mp4`;
    v.setAttribute("aria-hidden", "true");
    $(".card-media", card).append(v);
  }
  return v;
};
const playCard = (card) => {
  if (isPaused()) return;
  const v = ensureVideo(card);
  v.play().then(() => card.classList.add("is-playing")).catch(() => {});
};
const stopCard = (card) => {
  const v = $("video", card);
  if (v) v.pause();
  card.classList.remove("is-playing");
};
$$("[data-clip]", cards).forEach((card) => {
  if (hoverCapable) {
    card.addEventListener("pointerenter", () => playCard(card));
    card.addEventListener("pointerleave", () => stopCard(card));
  }
});
if (!hoverCapable) {
  const cardIO = new IntersectionObserver(
    (es) => es.forEach((e) => (e.intersectionRatio > 0.6 ? playCard(e.target) : stopCard(e.target))),
    { root: cards, threshold: [0, 0.6, 1] }
  );
  $$("[data-clip]", cards).forEach((c) => cardIO.observe(c));
}
$$("[data-scroll]").forEach((b) =>
  b.addEventListener("click", () => {
    const step = cards.querySelector(".card").getBoundingClientRect().width + 12;
    cards.scrollBy({ left: step * Number(b.dataset.scroll), behavior: reduced ? "auto" : "smooth" });
  })
);

/* ---------------------------------------------------------------- island map */
const mapEl = $("[data-map]");
new IntersectionObserver((es, io) => {
  if (!es[0].isIntersecting) return;
  io.disconnect();
  mountIslandMap(mapEl, { isPaused, list: $("[data-district-list]"), readout: $("[data-map-readout]") });
}, { rootMargin: "400px" }).observe(mapEl);

/* ---------------------------------------------------------------- origin lottery (same odds as the game) */
const ORIGINS = {
  insider: { rarity: "RARE / 20% CHANCE", name: "Insider Baby", line: "Born on the right side of the group chat.", cash: "$3,000", home: "Studio", debt: "Nothing", img: "insider_baby" },
  exit: { rarity: "COMMON / 80% CHANCE", name: "Exit Liquidity Baby", line: "Cooked on day one. Not your fault.", cash: "$300", home: "Parent's Basement", debt: "$720, $120 a week", img: "exit_liquidity_baby" },
};
const card = $("[data-card]");
const rollBtn = $("[data-roll]");
const result = $("[data-result]");
let rolling = false;
let rolls = 0;
rollBtn.addEventListener("click", async () => {
  if (rolling) return;
  rolling = true;
  const wait = (ms) => new Promise((r) => setTimeout(r, isPaused() ? 0 : ms));
  if (card.classList.contains("is-flipped")) {
    card.classList.remove("is-flipped");
    result.hidden = true;
    await wait(500);
  }
  card.classList.add("is-rolling");
  await wait(900);
  card.classList.remove("is-rolling");
  const o = rand(100) < 20 ? ORIGINS.insider : ORIGINS.exit;
  $("[data-card-img]").src = `assets/v2/origin/${o.img}.webp`;
  $("[data-card-img]").alt = `${o.name} origin card`;
  card.classList.add("is-flipped");
  await wait(550);
  $("[data-r-rarity]").textContent = o.rarity;
  $("[data-r-name]").textContent = o.name;
  $("[data-r-line]").textContent = o.line;
  $("[data-r-cash]").textContent = o.cash;
  $("[data-r-home]").textContent = o.home;
  $("[data-r-debt]").textContent = o.debt;
  result.hidden = false;
  rolls++;
  rollBtn.textContent = "Roll again";
  $("[data-roll-note]").textContent = rolls === 1 ? "In the game you only get one roll. Here, go wild." : `${rolls} lives and counting. The game won't be this nice.`;
  rolling = false;
});

/* ---------------------------------------------------------------- dialogs */
const dlgDownload = $('[data-dlg="download"]');
const dlgTeaser = $('[data-dlg="teaser"]');
const teaserVideo = $("video", dlgTeaser);
$$("[data-download]").forEach((a) => {
  if (DOWNLOAD_URL) {
    a.href = DOWNLOAD_URL;
    return;
  }
  a.addEventListener("click", (e) => {
    e.preventDefault();
    dlgDownload.showModal();
  });
});
$$("[data-teaser]").forEach((b) =>
  b.addEventListener("click", () => {
    if (dlgDownload.open) dlgDownload.close();
    dlgTeaser.showModal();
    teaserVideo.play().catch(() => {});
  })
);
$$("dialog").forEach((d) => {
  $("[data-close]", d).addEventListener("click", () => d.close());
  d.addEventListener("click", (e) => e.target === d && d.close());
});
dlgTeaser.addEventListener("close", () => teaserVideo.pause());

/* ---------------------------------------------------------------- nav state */
const navLinks = $$(".bar-nav a");
const navIO = new IntersectionObserver(
  (es) =>
    es.forEach((e) => {
      if (!e.isIntersecting) return;
      navLinks.forEach((a) => (a.getAttribute("href") === `#${e.target.id}` ? a.setAttribute("aria-current", "true") : a.removeAttribute("aria-current")));
    }),
  { rootMargin: "-45% 0px -50% 0px" }
);
navLinks.forEach((a) => {
  const t = $(a.getAttribute("href"));
  if (t) navIO.observe(t);
});

syncMotion();
