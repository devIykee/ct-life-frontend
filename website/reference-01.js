import { initReferenceMotion } from "./reference-motion.js";
import { initPreviewGallery } from "./campaign-preview.js";

initReferenceMotion();
document.body.classList.add("js-ready");
const gallery = initPreviewGallery();
const dialog = document.querySelector(".availability-dialog");
let downloadTrigger;
if (typeof dialog.showModal === "function") {
  document.body.classList.add("dialogs-ready");
  document.querySelectorAll("[data-download]").forEach((link) =>
    link.addEventListener("click", (event) => {
      event.preventDefault();
      downloadTrigger = link;
      dialog.showModal();
      document.body.classList.add("modal-open");
      dialog.querySelector(".dialog-close").focus();
    }),
  );
}
const closeAvailability = () => dialog.close();
dialog
  .querySelector(".dialog-close")
  .addEventListener("click", closeAvailability);
dialog.addEventListener("close", () => {
  document.body.classList.remove("modal-open");
  if (!document.querySelector("dialog[open]"))
    downloadTrigger?.focus({ preventScroll: true });
});
dialog.querySelector(".dialog-preview").addEventListener("click", () => {
  dialog.close();
  gallery.open(downloadTrigger);
});

const scenes = [
  {
    name: "TIMELINE SQUARE",
    title: "Find your people.",
    description:
      "The square is where your day starts. Walk the streets and open your in-game phone.",
    build: "V7 · 7 October 2026",
    file: "proof-timeline.webp",
    alt: "Seeker development capture of Timeline Square, with movement controls and the in-game phone. The image is marked Visual Review and Sample Data.",
  },
  {
    name: "CLIP CUT",
    title: "Find the good bit.",
    description:
      "One very long update. One short clip. Trim the setup and payoff into something worth watching.",
    build: "V7 · Local practice. No posting or payout.",
    file: "proof-clip-cut.webp",
    alt: "Seeker development capture of Clip Cut with eight storyboard frames and trim controls. Local Practice, No Posting or Payout and Sample Data labels remain visible.",
  },
  {
    name: "TERMINAL CAFÉ",
    title: "Take it offline. Ish.",
    description:
      "Pull up a chair at the café. There are terminals here too, so your thesis can come with you.",
    build: "V6 · 7 October 2026",
    file: "proof-cafe.webp",
    alt: "Seeker development capture of Terminal Café with tables and computer desks. The image is marked Visual Review and Sample Data.",
  },
];
const tabs = [...document.querySelectorAll("[data-scene]")];
const image = document.querySelector("#build-image");
const scenePanel = document.querySelector("#scene-panel");
const feedback = document.querySelector(".build-feedback");
const feedbackText = feedback.querySelector('[role="status"]');
const retry = document.querySelector(".build-retry");
let current = 0;
let version = 0;
function showFailure() {
  image.hidden = true;
  feedback.hidden = false;
  feedbackText.textContent = "This capture could not load.";
  retry.hidden = false;
  scenePanel.setAttribute("aria-busy", "false");
}
image.addEventListener("error", () => {
  if (version === 0) showFailure();
});
if (image.complete && image.naturalWidth === 0) showFailure();
retry.addEventListener("click", () => selectScene(current, false, true));
async function selectScene(index, focus = false, retryLoad = false) {
  current = (index + scenes.length) % scenes.length;
  const scene = scenes[current];
  const request = ++version;
  tabs.forEach((tab, i) => {
    tab.setAttribute("aria-selected", String(i === current));
    tab.tabIndex = i === current ? 0 : -1;
  });
  if (focus) tabs[current].focus({ preventScroll: true });
  if (document.activeElement === retry)
    tabs[current].focus({ preventScroll: true });
  document.querySelector("#build-caption").textContent = scene.name;
  document.querySelector("#build-index").textContent = `0${current + 1} / 03`;
  document.querySelector("#build-title").textContent = scene.title;
  document.querySelector("#build-description").textContent = scene.description;
  document.querySelector("#build-note").textContent = scene.build;
  scenePanel.setAttribute("aria-labelledby", tabs[current].id);
  scenePanel.setAttribute("aria-busy", "true");
  image.hidden = true;
  feedback.hidden = false;
  retry.hidden = true;
  feedbackText.textContent = "Loading capture…";
  const source = `assets/campaign-20261008/${scene.file}${retryLoad ? `?retry=${Date.now()}` : ""}`;
  const candidate = new Image();
  candidate.src = source;
  try {
    await candidate.decode();
  } catch {
    if (request !== version) return;
    showFailure();
    return;
  }
  if (request !== version) return;
  image.src = source;
  image.alt = scene.alt;
  try {
    await image.decode();
  } catch {
    if (request === version) showFailure();
    return;
  }
  if (request !== version) return;
  image.hidden = false;
  feedback.hidden = true;
  scenePanel.setAttribute("aria-busy", "false");
}
tabs.forEach((tab, index) => {
  tab.addEventListener("click", () => selectScene(index));
  tab.addEventListener("keydown", (event) => {
    const target = {
      ArrowRight: current + 1,
      ArrowLeft: current - 1,
      Home: 0,
      End: scenes.length - 1,
    }[event.key];
    if (target === undefined || event.altKey || event.ctrlKey || event.metaKey)
      return;
    event.preventDefault();
    selectScene(target, true);
  });
});
