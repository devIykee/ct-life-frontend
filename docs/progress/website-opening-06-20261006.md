# Website opening 06

6 October 2026. Local preview: `http://127.0.0.1:49223/draft-01/`.

The user rejected opening 05’s unrequested card-color changes, headline typography/hierarchy and story CTA. The original mint `#d0e7d7`, lavender `#d6c5ef` and blue `#c6e3eb` are restored in the hero and corresponding second-section surfaces. Black text, the light canvas and transparent logo alone remain.

The current type candidate uses solid Bungee Regular from the official Google Fonts repository. The headline remains “Welcome to Crypto Twitter”, visually set as WELCOME / TO CRYPTO / TWITTER. All three lines have one font, size and weight. The former half-size introduction is removed. Bungee also supplies the role labels, action lettering and second-section heading. Supporting text remains Manrope. The new type candidate awaits user review.

The user explicitly selected “Get the game; destination pending”. It is the single hero CTA, represented as a native disabled button without a fabricated destination. The header “A day in CT City” link and the old “Explore the game” story CTA are removed. The second section remains accessible by scrolling. No public download or playable URL was found in the checked website/design/handoff records; local development APKs were not exposed as releases.

## Research and verification

- Read the [designer’s official Bungee specimen](https://djr.com/bungee/), official Google Fonts metadata and SIL OFL 1.1 before download. The font, license, source release/commit and SHA-256 record are bundled in `website/draft-01/assets/fonts/`. No code package was added.
- Chrome inspection at 1468px desktop and 390px mobile. Additional width checks at 320px and 1024px showed no horizontal overflow and all headline lines fit.
- Bungee loaded successfully. At each checked width, all three headline lines computed to the same size and weight. At desktop, the size was 77.07px and weight 400, the actual weight of this heavy display face.
- Card surfaces computed to the original colors: `rgb(208,231,215)`, `rgb(214,197,239)` and `rgb(198,227,235)`. Visible text in the current preview computed to black.
- The accessibility tree reports one “Get the game” button, disabled. No story link remains in the header. Its pending state is documented without a new visible development badge.
- The two current entry points are byte-identical, referenced HTML files and anchors resolve locally, and the current preview contains no em dashes. Story logic was not changed; its existing keyboard checks from opening 05 were not repeated.
- An independent visual review found no further type adjustment necessary before user review. This is a design assessment, not user approval.
- Browser viewport overrides were reset and the final desktop tab was retained.

## Evidence and limits

- `website/draft-01/evidence/opening-06-hero-desktop.jpg`
- `website/draft-01/evidence/opening-06-mobile-full.jpg`
- `website/draft-01/assets/fonts/Bungee-source.json`
- `website/draft-01/opening-05-archive.html` and its two stylesheets preserve the superseded candidate.

No further website sections, game implementation, public deployment, remote push, payment or wallet changes were made. The real game-acquisition URL remains pending by the user’s decision. Existing promotional artwork remains conceptual. Production image-delivery optimization remains outstanding. Flutter checks do not apply to this static website.

Walrus recall succeeded in this turn. Palette/typography/CTA corrections and the explicit CTA decision were submitted to Walrus and accepted into its queue, but no completed upload was verified because the status tool was not exposed. Do not report those queued writes as saved.
