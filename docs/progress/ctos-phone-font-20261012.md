# CTOS dedicated phone typography — 2026-10-12

READY FOR COORDINATOR IMPORT: `client/Assets/Resources/UI/Fonts/Phone/` and `.meta`, the CTOS entry in `art/LICENSES.md`, and this note. Source verification passed; Unity import, actual phone rendering and Android verification remain coordinator-owned.

## Research and decision

The current [Apple font download page](https://developer.apple.com/fonts/) exposes its SF font agreement. Section 2B disallows using the downloaded font for non-Apple interface mockups and embedding it in products. The parallel research lane independently confirmed the same restriction and Apple's [font technology overview](https://developer.apple.com/documentation/technologyoverviews/fonts). No Apple font was downloaded or copied into the project.

The [Apple typography HIG](https://developer.apple.com/design/human-interface-guidelines/typography) was opened, but its web response requires JavaScript; detailed sizing research is owned by the research lane rather than inferred from the empty response. The visual objective is neutral, legible phone typography with clear weight hierarchy, not CT Life's expressive game titles.

Selected **Inter 4.1**, by The Inter Project Authors. The [official site](https://rsms.me/inter/) describes its text optical size's tall x-height and small-text contrast details. [The official latest release](https://github.com/rsms/inter/releases/latest) resolved to v4.1 when checked; the GitHub release API reports publication on 2024-11-16. [Its OFL](https://raw.githubusercontent.com/rsms/inter/v4.1/LICENSE.txt) permits embedding and redistribution with the copyright and licence retained. Inter is the chosen licensed alternative; it is not SF Pro and no exact SF metric equivalence is claimed.

## Shipped resources

All paths below are `Resources.Load<Font>` paths without extensions:

| Resource | Weight | Recommended role |
| --- | --- | --- |
| `UI/Fonts/Phone/Inter-Regular` | 400 | App body text and quiet launcher labels |
| `UI/Fonts/Phone/Inter-Medium` | 500 | Status and compact control labels |
| `UI/Fonts/Phone/Inter-SemiBold` | 600 | App titles and strong numerical emphasis |

USS files under `Resources/UI` can use `url("Fonts/Phone/Inter-Regular.ttf")`, and the matching Medium/SemiBold paths. Avoid synthetic bold on an already weighted face. This lane changes no game font or stylesheet.

Each font is an unmodified upstream static TTF from `Inter-4.1.zip` → `extras/ttf/`, using the text design rather than the Display family. The three files total 1,248,684 bytes. The official release already supplied these statics; no conversion, package installation or generated derivative was needed. Local Python lacks fontTools, so inspection used standard-library SFNT table parsing instead of adding a dependency.

`UI/Fonts/Phone/Inter-OFL` contains the complete exact archive licence. `font-sources.json` records the release, archive and per-file SHA-256 hashes, source entries, author, date and measured metadata. The in-app credits entry is requested from the coordinator, who owns `AssetCredits.cs` and shared strings.

## Verification and integration notes

- All three copied files match their official source hashes; weight metadata is 400/500/600; none contains a variable `fvar` table.
- All contain 2,937 glyphs and consistent metrics: 2,048 units/em, ascent 1,984, descent −494, line gap 0, x-height 1,118 and cap height 1,490. These are measured font values, not assumed SF values.
- Unicode mapping includes printable ASCII, typographic quotes, ellipsis, en/em dash, bullet, multiplication, naira, euro and pound characters. This does not claim every language or emoji is supported.
- The source release tag is v4.1 while the embedded font version is `Version 4.001;git-9221beed3`; the manifest records both faithfully.
- Added Unity `.meta` importers follow the existing local TTF import settings, with each face's actual family name and embedded source data. [Unity 6.3 font asset documentation](https://docs.unity3d.com/6000.3/Documentation/Manual/UIE-font-asset.html), opened before integration, supports TrueType sources and `-unity-font-definition`; dynamic font assets retain their source for newly encountered text.
- The research lane recommends launcher labels around one fifth of icon width, neutral tracking, and no text outline. Actual Seeker legibility should decide the final size; this font preparation does not establish that the final layout is legible.

No Unity, ADB, build or Git command was run by this lane. Coordinator acceptance remains: import all three faces, check resolved phone font assignments, inspect launcher/app screenshots, and verify device text clipping/fallback. Font licence access must appear in existing Credits & licenses.
