// CT Island drawn from the game's own world data (tools/world/islandgen/out):
// terrain contours, coast, roads and every building block as a wireframe, seen
// in perspective. Drag to turn; tap a district to read what's there.

const CENTER = [25, 0];
const PITCH = 0.86; // radians from horizontal
const DIST = 2600;

const NAMES = { penthouse_row: "Penthouse Row", yacht_club: "Yacht Club", checker_kiosk: "Checker Kiosk" };
const LINES = {
  timeline: "The conversation starts here.",
  trenches: "Big conviction. Bigger plot twists.",
  boardwalk: "Sand, the arcade pier and the Prediction Pit.",
  dubai_marina: "Dress up. Dream big. Stay awhile.",
  airdrop_farm: "Points are planted here.",
  builder_hills: "Shipping while you scroll.",
  breakpoint: "Conference season, all season.",
  residences: "Seed Phrase Lane, Cold Wallet Crescent and your front door.",
  mountain: "Basecamp, the ORE Refinery and the summit.",
  islet: "A palm islet at the end of the causeway.",
};

export async function mountIslandMap(el, { isPaused, list, readout }) {
  const data = await fetch("assets/v2/map/island.json").then((r) => r.json());
  const canvas = el.querySelector("canvas");
  const ctx = canvas.getContext("2d");

  const venuesBy = {};
  data.venues.forEach((v) => (venuesBy[v.d] ||= []).push(NAMES[v.id] || v.name));

  let W = 0, H = 0, dpr = 1, k = 1;
  const resize = () => {
    dpr = Math.min(window.devicePixelRatio || 1, 2);
    W = el.clientWidth;
    H = el.clientHeight;
    canvas.width = Math.round(W * dpr);
    canvas.height = Math.round(H * dpr);
    k = W < 700 ? W / 1180 : Math.min(W / 1760, H / 1130);
  };
  new ResizeObserver(resize).observe(el);
  resize();

  let yaw = -0.35, yawVel = 0, drag = null, selected = null, hover = null;
  let cy = 1, sy = 0;
  const cp = Math.cos(PITCH), sp = Math.sin(PITCH);
  // world (x east, h up, z north) -> screen
  const P = (x, h, z) => {
    const dx = x - CENTER[0], dz = z - CENTER[1];
    const rx = dx * cy - dz * sy;
    const rz = dx * sy + dz * cy;
    const yc = h * cp + rz * sp;
    const zc = -h * sp + rz * cp;
    const s = DIST / (DIST + zc);
    return [W / 2 + rx * s * k, H * 0.5 - yc * s * k];
  };

  const polyline = (arr, start, h) => {
    for (let i = start; i < arr.length; i += 2) {
      const [x, y] = P(arr[i], h, arr[i + 1]);
      i === start ? ctx.moveTo(x, y) : ctx.lineTo(x, y);
    }
  };

  const pip = (pt, poly) => {
    let c = false;
    for (let i = 0, j = poly.length - 1; i < poly.length; j = i++) {
      const [xi, yi] = poly[i], [xj, yj] = poly[j];
      if (yi > pt[1] !== yj > pt[1] && pt[0] < ((xj - xi) * (pt[1] - yi)) / (yj - yi) + xi) c = !c;
    }
    return c;
  };
  const inDistrict = (x, z, d) => pip([x, z], d.poly);
  const boxDistrict = data.boxes.map((b) => data.districts.find((d) => inDistrict(b[0], b[2], d))?.id || null);

  // screen-space hit test for districts
  let screenPolys = [];
  const pick = (mx, my) => screenPolys.find((s) => pip([mx, my], s.pts))?.d || null;

  const select = (d) => {
    selected = selected && d && selected.id === d.id ? null : d;
    [...list.querySelectorAll("button")].forEach((b) => b.setAttribute("aria-pressed", String(!!selected && b.dataset.id === selected.id)));
    if (!selected) {
      readout.textContent = "";
      return;
    }
    const v = venuesBy[selected.id] || [];
    readout.innerHTML = "";
    const b = document.createElement("b");
    b.textContent = selected.name;
    const p = document.createElement("p");
    p.textContent = LINES[selected.id] || "";
    readout.append(b, p);
    if (v.length) {
      const s = document.createElement("span");
      s.textContent = v.join(" / ");
      readout.append(s);
    }
  };

  data.districts.forEach((d) => {
    const li = document.createElement("li");
    const b = document.createElement("button");
    b.type = "button";
    b.dataset.id = d.id;
    b.textContent = d.name;
    b.setAttribute("aria-pressed", "false");
    b.addEventListener("click", () => select(d));
    li.append(b);
    list.append(li);
  });

  el.addEventListener("pointerdown", (e) => {
    drag = { x: e.clientX, yaw, moved: 0 };
    el.setPointerCapture(e.pointerId);
  });
  el.addEventListener("pointermove", (e) => {
    const r = el.getBoundingClientRect();
    if (drag) {
      const dx = e.clientX - drag.x;
      drag.moved = Math.max(drag.moved, Math.abs(dx));
      yaw = drag.yaw + dx * 0.006;
      return;
    }
    hover = pick(e.clientX - r.left, e.clientY - r.top);
    el.style.cursor = hover ? "pointer" : "";
  });
  el.addEventListener("pointerup", (e) => {
    const r = el.getBoundingClientRect();
    if (drag && drag.moved < 6) select(pick(e.clientX - r.left, e.clientY - r.top));
    drag = null;
  });
  el.addEventListener("pointerleave", () => (hover = null));

  let visible = true;
  new IntersectionObserver(([e]) => (visible = e.isIntersecting)).observe(el);

  const LILAC = "196,176,255";
  const PURPLE = "153,69,255";
  const GREEN = "20,241,149";

  const draw = (t) => {
    cy = Math.cos(yaw);
    sy = Math.sin(yaw);
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    ctx.clearRect(0, 0, W, H);
    ctx.lineJoin = "round";

    // terrain contours, brighter as they climb
    for (const c of data.contours) {
      const h = c[0];
      const a = 0.12 + Math.min(1, (h - data.sea) / 180) * 0.32;
      ctx.strokeStyle = `rgba(${PURPLE},${a.toFixed(3)})`;
      ctx.lineWidth = 1;
      ctx.beginPath();
      polyline(c, 1, h);
      ctx.stroke();
    }

    // coast
    ctx.strokeStyle = `rgba(${LILAC},0.85)`;
    ctx.lineWidth = 1.4;
    ctx.beginPath();
    polyline(data.coast, 0, data.sea);
    ctx.closePath();
    ctx.stroke();

    // water
    ctx.strokeStyle = "rgba(120,200,255,0.55)";
    ctx.lineWidth = 1.6;
    ctx.beginPath();
    data.river.forEach(([x, z], i) => {
      const [sx, sy2] = P(x, 0, z);
      i ? ctx.lineTo(sx, sy2) : ctx.moveTo(sx, sy2);
    });
    ctx.stroke();

    // district fills for hover / selection, and screen polys for picking
    screenPolys = data.districts.map((d) => ({ d, pts: d.poly.map(([x, z]) => P(x, 4, z)) }));
    for (const s of screenPolys) {
      const on = selected && selected.id === s.d.id;
      const hv = hover && hover.id === s.d.id;
      if (!on && !hv) continue;
      ctx.beginPath();
      s.pts.forEach(([x, y], i) => (i ? ctx.lineTo(x, y) : ctx.moveTo(x, y)));
      ctx.closePath();
      ctx.fillStyle = on ? `rgba(${GREEN},0.10)` : `rgba(${LILAC},0.07)`;
      ctx.fill();
      ctx.strokeStyle = on ? `rgba(${GREEN},0.7)` : `rgba(${LILAC},0.4)`;
      ctx.setLineDash([4, 5]);
      ctx.stroke();
      ctx.setLineDash([]);
    }

    // roads
    for (const r of data.roads) {
      ctx.strokeStyle = r[0] === 2 ? `rgba(${LILAC},0.9)` : `rgba(${LILAC},0.55)`;
      ctx.lineWidth = r[0] === 2 ? 1.8 : r[0] === 1 ? 1.3 : 0.9;
      ctx.beginPath();
      polyline(r, 2, r[1]);
      ctx.stroke();
    }

    // buildings: wireframe blocks
    const pulse = 0.5 + 0.5 * Math.sin(t * 2);
    for (let pass = 0; pass < 2; pass++) {
      ctx.beginPath();
      for (let i = 0; i < data.boxes.length; i++) {
        const lit = selected && boxDistrict[i] === selected.id;
        if ((pass === 1) !== !!lit) continue;
        const [bx, by, bz, w, h, d, yd] = data.boxes[i];
        const a = (yd * Math.PI) / 180, ca = Math.cos(a), sa = Math.sin(a);
        const corners = [[-w / 2, -d / 2], [w / 2, -d / 2], [w / 2, d / 2], [-w / 2, d / 2]].map(([ox, oz]) => [bx + ox * ca + oz * sa, bz - ox * sa + oz * ca]);
        const lo = corners.map(([x, z]) => P(x, by, z));
        const hi = corners.map(([x, z]) => P(x, by + h, z));
        for (let j = 0; j < 4; j++) {
          const n = (j + 1) % 4;
          ctx.moveTo(lo[j][0], lo[j][1]); ctx.lineTo(lo[n][0], lo[n][1]);
          ctx.moveTo(hi[j][0], hi[j][1]); ctx.lineTo(hi[n][0], hi[n][1]);
          ctx.moveTo(lo[j][0], lo[j][1]); ctx.lineTo(hi[j][0], hi[j][1]);
        }
      }
      ctx.strokeStyle = pass ? `rgba(${GREEN},${(0.65 + pulse * 0.3).toFixed(2)})` : `rgba(${PURPLE},0.8)`;
      ctx.lineWidth = pass ? 1.1 : 0.8;
      ctx.stroke();
    }

    // venues: little markers, like pins in a field report
    ctx.font = `500 10px "Martian Mono", monospace`;
    ctx.textBaseline = "middle";
    for (const v of data.venues) {
      const [x, y] = P(v.at[0], v.y + 18, v.at[1]);
      const on = selected && selected.id === v.d;
      ctx.fillStyle = on ? `rgb(${GREEN})` : "rgba(232,255,120,0.9)";
      ctx.beginPath();
      ctx.moveTo(x, y - 5); ctx.lineTo(x + 4.5, y + 3); ctx.lineTo(x - 4.5, y + 3); ctx.closePath();
      ctx.fill();
      if (on) {
        ctx.fillStyle = "#fff";
        ctx.fillText((NAMES[v.id] || v.name).toUpperCase(), x + 9, y);
      }
    }
    // statues and landmark nods
    for (const s of data.statues) {
      const [x, y] = P(s.at[0], 12, s.at[1]);
      ctx.strokeStyle = "rgba(255,150,80,0.95)";
      ctx.lineWidth = 1.4;
      ctx.beginPath();
      ctx.arc(x, y, 3.5, 0, Math.PI * 2);
      ctx.stroke();
    }

    // district labels
    ctx.font = `600 ${W < 700 ? 8.5 : 11}px "Martian Mono", monospace`;
    ctx.textAlign = "center";
    for (const d of data.districts) {
      const [x, y] = P(d.at[0], 40, d.at[1]);
      const on = (selected && selected.id === d.id) || (hover && hover.id === d.id);
      const label = d.name.toUpperCase();
      const tw = ctx.measureText(label).width + 14;
      ctx.fillStyle = on ? `rgb(${GREEN})` : "rgba(7,8,10,0.72)";
      ctx.fillRect(x - tw / 2, y - (W < 700 ? 8 : 10), tw, W < 700 ? 16 : 20);
      ctx.fillStyle = on ? "#07080a" : `rgb(${LILAC})`;
      ctx.fillText(label, x, y + 0.5);
    }
    ctx.textAlign = "start";
  };

  const start = performance.now();
  let last = start;
  const frame = (now) => {
    requestAnimationFrame(frame);
    if (!visible) return;
    const dt = Math.min(0.05, (now - last) / 1000);
    last = now;
    const t = (now - start) / 1000;
    if (!drag && !isPaused()) {
      // rock gently around the current heading so labels stay readable
      yawVel += (Math.sin(t * 0.12) * 0.07 - yawVel) * dt;
      yaw += yawVel * dt;
    }
    draw(isPaused() ? 0 : t);
  };
  requestAnimationFrame(frame);
}
