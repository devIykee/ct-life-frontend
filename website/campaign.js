import { inject } from '@vercel/analytics';
import { initPreviewGallery } from "./campaign-preview.js";
import { initCampaignMotion } from "./campaign-motion.js";

// Initialize Vercel Web Analytics
inject();

const cityScenes = {
  social: {
    number: "01",
    place: "CAFÉ & SPACES STUDIO",
    title: ["Pull up.", "Speak up."],
    copy: "Catch up at the café. Join a Space. Raise your hand when you have something worth saying.",
    quote: ["Conviction: high.", "Microphone: muted."],
    note: "",
  },
  work: {
    number: "02",
    place: "JOBS & PROJECTS",
    title: ["Earn", "your keep."],
    copy: "Handle the mod queue. Cut the clip. Turn a messy idea into a thread. There’s work to do before you become a legend.",
    quote: ["One quick point.", "A very long clip."],
    note: "",
  },
  markets: {
    number: "03",
    place: "THE TERMINALS",
    title: ["Make a call.", "Live with it."],
    copy: "Trade made-up coins. Compare notes. Celebrate a good call, then face the group chat when the next one goes sideways.",
    quote: ["Different thesis.", "Same group chat."],
    note: "Fictional markets. In-game cash cannot be withdrawn.",
  },
  home: {
    number: "04",
    place: "YOUR CORNER OF THE CITY",
    title: ["Life happens.", "Go live it."],
    copy: "Eat, sleep, change your look and find a place that feels like yours. There’s more to life than the next candle.",
    quote: ["Rent’s due.", "Stay bullish."],
    note: "",
  },
};
const personalities = {
  builder: {
    name: "The builder.",
    quote: ["“One small change", "before shipping.”"],
    bio: "Shipping fixes. Forgetting lunch.",
    object: ["{", "✳", "}"],
    number: "001",
  },
  trencher: {
    name: "The trencher.",
    quote: ["“I was early.", "Ask the group chat.”"],
    bio: "Twenty tabs. One very strong opinion.",
    object: ["↗", "↘"],
    number: "002",
  },
  caller: {
    name: "The caller.",
    quote: ["“Am I muted?", "Anyway. My thesis.”"],
    bio: "Conviction: high. Microphone: optional.",
    object: ["◖", "✳", "◗"],
    number: "003",
  },
  farmer: {
    name: "The airdrop farmer.",
    quote: ["“Just one more", "daily check-in.”"],
    bio: "Growing points. Occasionally vegetables.",
    object: ["✳", "✳"],
    number: "004",
  },
  anon: {
    name: "The anon.",
    quote: ["“You wouldn’t", "know my main.”"],
    bio: "Anonymous online. Known to the landlord.",
    object: ["?"],
    number: "005",
  },
  perp: {
    name: "The perp degen.",
    quote: ["“This time,", "I have a plan.”"],
    bio: "Always has a thesis. Sometimes has lunch.",
    object: ["↑", "↓"],
    number: "006",
  },
  shark: {
    name: "The prediction shark.",
    quote: ["“Interesting odds.", "I’ll take the other side.”"],
    bio: "Reading the room. Fading the room.",
    object: ["½"],
    number: "007",
  },
};

const setLines = (element, lines) => {
  element.replaceChildren(
    ...lines.flatMap((line, index) =>
      index
        ? [document.createElement("br"), document.createTextNode(line)]
        : [document.createTextNode(line)],
    ),
  );
};
const allowMotion = () => document.documentElement.dataset.motion === "full";
// Cancel user-interface effects as soon as the site or OS motion preference changes.
const uiAnimations = new Set();
function runAnimation(element, frames, options) {
  if (!allowMotion() || typeof element.animate !== "function") return;
  const animation = element.animate(frames, options);
  uiAnimations.add(animation);
  animation.finished.then(
    () => uiAnimations.delete(animation),
    () => uiAnimations.delete(animation),
  );
}
new MutationObserver(() => {
  if (!allowMotion()) {
    uiAnimations.forEach((animation) => animation.cancel());
    uiAnimations.clear();
  }
}).observe(document.documentElement, {
  attributes: true,
  attributeFilter: ["data-motion"],
});
const animateSwap = (element) => {
  if (!allowMotion() || typeof element.animate !== "function") return;
  element.getAnimations().forEach((animation) => animation.cancel());
  runAnimation(
    element,
    [
      { opacity: 0.3, translate: "0 9px" },
      { opacity: 1, translate: "0 0" },
    ],
    { duration: 260, easing: "cubic-bezier(.2,.75,.25,1)" },
  );
};
const cityTabs = [...document.querySelectorAll(".city-tabs [data-city]")];
const cityPins = [...document.querySelectorAll(".city-pin")];
const cityFrame = document.querySelector(".city-frame");
const cityPanel = document.querySelector("#city-panel");
function selectCity(key, focus = false) {
  const scene = cityScenes[key];
  if (!scene) return;
  cityFrame.dataset.selected = key;
  cityTabs.forEach((tab) => {
    const selected = tab.dataset.city === key;
    tab.setAttribute("aria-selected", String(selected));
    tab.tabIndex = selected ? 0 : -1;
    if (selected && focus) tab.focus({ preventScroll: true });
  });
  cityPins.forEach((pin) => {
    const selected = pin.dataset.city === key;
    pin.classList.toggle("is-selected", selected);
    pin.setAttribute("aria-pressed", String(selected));
  });
  cityPanel.setAttribute("aria-labelledby", `city-tab-${key}`);
  document.querySelector("#scene-number").textContent = scene.number;
  document.querySelector("#scene-place").textContent = scene.place;
  setLines(document.querySelector("#scene-title"), scene.title);
  document.querySelector("#scene-copy").textContent = scene.copy;
  setLines(document.querySelector("#scene-quote"), scene.quote);
  document.querySelector("#scene-note").textContent = scene.note;
  animateSwap(cityPanel);
}
[...cityTabs, ...cityPins].forEach((button) =>
  button.addEventListener("click", () => selectCity(button.dataset.city)),
);
function tabKeyboard(event, tabs, select, keyName, vertical = false) {
  let index = tabs.indexOf(event.currentTarget);
  if (event.key === "Home") index = 0;
  else if (event.key === "End") index = tabs.length - 1;
  else if (
    event.key === "ArrowRight" ||
    (vertical && event.key === "ArrowDown")
  )
    index = (index + 1) % tabs.length;
  else if (event.key === "ArrowLeft" || (vertical && event.key === "ArrowUp"))
    index = (index + tabs.length - 1) % tabs.length;
  else return;
  event.preventDefault();
  select(tabs[index].dataset[keyName], true);
}
cityTabs.forEach((tab) =>
  tab.addEventListener("keydown", (event) =>
    tabKeyboard(event, cityTabs, selectCity, "city"),
  ),
);

let selectedPersonality = "builder";
let feedbackTimeout;
const personalityTabs = [...document.querySelectorAll("[data-type]")];
const personalityPanel = document.querySelector("#personality-panel");
const personalityList = document.querySelector(".type-list");
const narrowTypes = matchMedia("(max-width: 800px)");
function updateTypeOrientation() {
  personalityList.setAttribute(
    "aria-orientation",
    narrowTypes.matches ? "horizontal" : "vertical",
  );
}
updateTypeOrientation();
narrowTypes.addEventListener("change", updateTypeOrientation);
function selectPersonality(key, focus = false) {
  const personality = personalities[key];
  if (!personality) return;
  selectedPersonality = key;
  personalityTabs.forEach((tab) => {
    const selected = tab.dataset.type === key;
    tab.setAttribute("aria-selected", String(selected));
    tab.tabIndex = selected ? 0 : -1;
    if (selected && focus) {
      tab.focus({ preventScroll: true });
      if (narrowTypes.matches) {
        const left =
          tab.offsetLeft -
          personalityList.offsetLeft -
          (personalityList.clientWidth - tab.clientWidth) / 2;
        personalityList.scrollTo({
          left,
          behavior: allowMotion() ? "smooth" : "instant",
        });
      }
    }
  });
  personalityPanel.dataset.personality = key;
  personalityPanel.setAttribute("aria-labelledby", `type-${key}`);
  setLines(document.querySelector("#personality-quote"), personality.quote);
  document.querySelector("#personality-name").textContent = personality.name;
  document.querySelector("#personality-bio").textContent = personality.bio;
  document.querySelector("#personality-serial").textContent =
    `CT / ${personality.number}`;
  const object = document.querySelector("#personality-object");
  object.replaceChildren(
    ...personality.object.map((symbol) => {
      const span = document.createElement("span");
      span.className = symbol === "✳" ? "object-asterisk" : "code-bracket";
      span.textContent = symbol;
      return span;
    }),
  );
  clearTimeout(feedbackTimeout);
  document.querySelector(".copy-feedback").textContent = "";
  animateSwap(personalityPanel.querySelector("blockquote"));
  animateSwap(object);
}
personalityTabs.forEach((tab) => {
  tab.addEventListener("click", () => selectPersonality(tab.dataset.type));
  tab.addEventListener("keydown", (event) =>
    tabKeyboard(
      event,
      personalityTabs,
      selectPersonality,
      "type",
      !narrowTypes.matches,
    ),
  );
});
document.querySelector(".copy-bio").addEventListener("click", async () => {
  const personality = personalities[selectedPersonality];
  const bio = `My CT Life: ${personality.name} ${personality.bio}`;
  const feedback = document.querySelector(".copy-feedback");
  clearTimeout(feedbackTimeout);
  try {
    if (!navigator.clipboard?.writeText)
      throw new Error("Clipboard unavailable");
    await navigator.clipboard.writeText(bio);
    feedback.textContent = "Copied. The group chat should know.";
    feedbackTimeout = setTimeout(() => {
      feedback.textContent = "";
    }, 4500);
  } catch {
    feedback.textContent = `Copy this: ${bio}`;
  }
});

const menuButton = document.querySelector(".menu-button");
const mobileNav = document.querySelector("#mobile-nav");
function closeMenu(restoreFocus = false) {
  mobileNav.hidden = true;
  menuButton.setAttribute("aria-expanded", "false");
  menuButton.setAttribute("aria-label", "Open navigation");
  if (restoreFocus) menuButton.focus();
}
menuButton.addEventListener("click", () => {
  const expanded = menuButton.getAttribute("aria-expanded") === "true";
  mobileNav.hidden = expanded;
  menuButton.setAttribute("aria-expanded", String(!expanded));
  menuButton.setAttribute(
    "aria-label",
    expanded ? "Open navigation" : "Close navigation",
  );
  if (!expanded) animateSwap(mobileNav);
});
mobileNav
  .querySelectorAll("a")
  .forEach((link) => link.addEventListener("click", () => closeMenu()));
document.addEventListener("keydown", (event) => {
  if (event.key === "Escape" && !mobileNav.hidden) closeMenu(true);
});
document.addEventListener("click", (event) => {
  if (!mobileNav.hidden && !event.target.closest(".site-header")) closeMenu();
});
narrowTypes.addEventListener("change", (event) => {
  if (!event.matches) closeMenu();
});

const dialog = document.querySelector("#download-dialog");
let lastDownloadTrigger;
let downloadDestination;
document.querySelectorAll("[data-download]").forEach((link) =>
  link.addEventListener("click", (event) => {
    if (typeof dialog.showModal !== "function") return;
    event.preventDefault();
    closeMenu();
    lastDownloadTrigger = link;
    dialog.showModal();
    document.body.classList.add("dialog-open");
    runAnimation(
      dialog,
      [
        { opacity: 0, translate: "0 12px", scale: ".98" },
        { opacity: 1, translate: "0 0", scale: "1" },
      ],
      { duration: 240, easing: "cubic-bezier(.2,.75,.25,1)" },
    );
  }),
);
dialog
  .querySelector(".dialog-close")
  .addEventListener("click", () => dialog.close());
dialog.addEventListener("click", (event) => {
  if (event.target !== dialog) return;
  const rect = dialog.getBoundingClientRect();
  if (
    event.clientX < rect.left ||
    event.clientX > rect.right ||
    event.clientY < rect.top ||
    event.clientY > rect.bottom
  )
    dialog.close();
});
dialog.addEventListener("close", () => {
  document.body.classList.remove("dialog-open");
  const destination = downloadDestination || lastDownloadTrigger;
  downloadDestination = undefined;
  if (destination?.isConnected) destination.focus({ preventScroll: true });
});
dialog.querySelector("[data-close-download]").addEventListener("click", () => {
  downloadDestination = document.querySelector("#city-title");
  dialog.close();
});
document.querySelectorAll("details").forEach((details) =>
  details.addEventListener("toggle", () => {
    if (details.open) animateSwap(details.querySelector(".faq-answer"));
  }),
);

initCampaignMotion();
initPreviewGallery();
