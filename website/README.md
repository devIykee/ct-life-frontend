# CT Life companion website

Dependency-free static companion site. No package installation, build step, backend, login, payments, analytics, public download or playable demo. Nothing is deployed.

## Run locally

From the repository root:

```sh
python3 -m http.server 4178 --bind 127.0.0.1 --directory website
```

Open http://127.0.0.1:4178/ . Stop with Ctrl-C. Binding is loopback only.

## Files

- `index.html`: progressive-enhancement landing page with meaningful default content.
- `styles.css`: blue/gold game identity, local fonts, responsive layouts and reduced-motion treatment.
- `app.js`: district/look/status keyboard tabs, mobile navigation, native screenshot dialog, motion control.
- `credits.html`: visible credits, modification notices and full locally bundled licenses.
- `assets/manifest.json`: every copied asset’s original repo path, size and SHA256.
- `evidence/`: browser screenshots from actual local review (not gameplay evidence).

All fonts, images and scripts are local. Optional credit links lead to source/license pages only. No third-party script or embed. Images are copied unchanged; character artwork is labeled as studio studies and the Seeker screenshots retain the development review strip/sample values.

## Copy truth

- The current client has a lobby, venues, movement, authored looks, phone and MWA sign-in integration; it still requires live backend validation.
- ORE staking/board/payments and SKR payments remain planned. `.skr` names may be displayed from backend data.
- Standard creation as the free route is a plan; optional generation pricing/provider/currency is unresolved. No payment mechanism is invented here.
- No player counts, testimonials, release date, download link or claimed multiplayer/payment demonstration.

Responsive and keyboard verification is recorded in `../docs/progress/website-quality.md`.
