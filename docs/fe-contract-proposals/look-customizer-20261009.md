# Look customizer: how selections map to the contract (FE proposal, 2026-10-09)

Owner: onboarding UI lead. Status: **proposal; nothing in the contract changes today.**

The creator now has a Snapchat/Bitmoji-style customizer. Step 1 is Character (the base model). Step 2 is Customize, a
category rail: Skin tone, Hair, Hair colour, Top, Bottom, Shoes, Accessories, Colours. It is data-driven:
`client/Assets/Scripts/UI/Screens/Onboarding/Customizer.cs` defines `CustomizerCatalog` (asset `Resources/UI/CustomizerCatalog`,
optional) and `CustomizerModel`.

## Today: no contract change
Every option is a patch on the seven existing `Look` ids, `[skinTone, headId, hairId, topId, bottomId, shoesId, accId]`.
`POST /v1/characters` keeps its exact shape.

| Category | Look field it changes | How |
|---|---|---|
| Character (base model) | all seven | the base model's default look |
| Skin tone | `skinTone` | a skin id, e.g. `skin_fair` |
| Hair | `hairId` | a hair style id |
| Hair colour | `hairId` | a colour variant id of the current style, e.g. `hair_bob__ginger` |
| Top / Bottom / Shoes | `topId` / `bottomId` / `shoesId` | item ids |
| Accessories | `accId` | an item id |
| Colours | `topId` / `bottomId` | colour variant ids, e.g. `top_hoodie__purple` |

What the backend needs for this to submit:
- Every variant id must exist in `items.json` with `starter: true` (or whatever marks free creator items). Otherwise
  `POST /v1/characters` rejects it (see feedback #42 and #94).
- The variant naming convention is `<base item id>__<variant>`, e.g. `hair_bob__ginger`. The FE treats it as an opaque
  id; the backend only needs to list it.

## If the backend prefers palettes over variant ids (optional, later)
Variant ids multiply items: styles × colours. If that becomes too many rows, an additive change would work:
- `Look.hairColor?: string` (palette id from a new `ids/palettes.json`);
- `Look.colors?: { top?: string, bottom?: string }`.
Both are optional, so old clients keep working. The FE would then map Hair colour and Colours to these fields instead of
variant ids. Not needed until the multi-model cast lands with per-slot parts.

## What the client does today with the current cast
The 24 creator looks are complete authored characters with no per-slot parts. So every base model has zero customizer
options: the categories hide themselves, and the Customize step is skipped. A category only shows when it offers at least
two distinct looks that resolve to real art: an exact registry look, or a modular set via `ItemCatalog`.
