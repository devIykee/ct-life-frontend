# City Life: NFT gallery wall in homes

[Proposal] Additive. Players frame NFTs they actually hold on their home's walls; visitors see them. Ownership is checked
by the server (wallet from SIWS), images come through our image proxy, and every image carries a moderation verdict.
The client is built (`Logic/NftGallery.cs`, `Net/NftImageCache.cs`, `UI/NftGalleryEditor.cs`, `UI/CityLifeOverlays.cs`
`NftWallPresenter`) and shows "not live on this server yet" until these routes exist.

## 1. Endpoints

```
GET /nfts/me?limit=100&cursor=          → { wallet, items: NftItem[], cursor? }        (my verified holdings)
GET /homes/{ownerId}/gallery            → { frames: [{slot, assetId}], items: NftItem[] } (what hangs on that home's walls)
PUT /homes/me/gallery  {frames: [{slot, assetId}]}  (Idempotency-Key) → same as GET for me

NftItem { assetId, name, collection, imageUrl, thumbUrl?, mime, width?, height?, moderation: ok | pending | nsfw | blocked }
```
- `slot` is 1-based and must be `<= Home.frameSlots` (city-life-homes-needs.md 2a); the server rejects assets the wallet
  does not hold at save time (`FORBIDDEN {reason: "not_owner"}`) and re-checks periodically, removing frames whose
  asset left the wallet or was burnt.
- `GET /homes/{ownerId}/gallery` for a visitor returns only `moderation: "ok"` items; for the owner it also returns
  `pending` (shown as a grey placeholder to the owner only). `nsfw` / `blocked` are never returned to visitors and are
  never fetched by the client.
- Privacy: a gallery reveals holdings. Frames are opt-in (empty by default), and `visitors` policy (homes doc 2c) gates
  who can walk in. [Inference] Showing `collection` + `name` but never the wallet address to visitors is enough for the flex.

## 2. How the server reads holdings (verified)

Helius DAS `getAssetsByOwner` (`ownerAddress`, `page` or `before/after`, `limit` up to 1000, `sortBy`, `displayOptions`
incl. `showCollectionMetadata`, `showUnverifiedCollections`). Image-relevant response fields: `content.links.image`,
`content.files[]` with `uri`, `cdn_uri`, `mime`, `content.metadata`, `grouping` (collection), `burnt`, `interface`.
**DAS has no NSFW / spam / moderation flag** (checked on the Helius docs page 2026-10-08), so `moderation` must be our own
verdict: e.g. a collection allow-list for `ok`, everything else `pending` until a classifier or manual review passes it.
Suggested filters: drop `burnt`, fungible interfaces and unverified collections by default.

## 3. Images

- The server returns `imageUrl` / `thumbUrl` on **our image proxy** (re-encoded PNG/JPEG, EXIF stripped, ≤ 512 px,
  ≤ 512 KB), never the raw Arweave/IPFS/https URL from metadata: raw hosts leak player IPs to arbitrary third parties, can
  swap content after review, and may serve GIF/WebP/SVG that Unity's `Texture2D.LoadImage` does not decode.
- Config `nft`: `{ "imageHosts": ["img.<our-domain>"], "maxImageBytes": 524288, "maxImagePx": 512 }`. The client fetches
  HTTPS only, from listed hosts only (exact host, or `.suffix` for subdomains), with no redirects, aborts on a declared or
  streamed size over `maxImageBytes`, decodes PNG/JPEG only, rejects images over `maxImagePx`, and keeps decoded textures
  in a 48 MB LRU (12 frames at the 1024 px client cap). With no `imageHosts` configured nothing is fetched.

## 4. What PFP culture looks like on Solana now (why this is worth building)

Floors read from Magic Eden's public stats API on 2026-10-08 (lamports → SOL): Solana Monkey Business 12.65, Mad Lads
8.50, Claynosaurz 7.20, Okay Bears 1.28, Retardio Cousins 0.72 (7-day volume 323 / 802 / 333 / 52 / 179 SOL). [Inference]
Blue-chip PFPs still trade weekly at multi-SOL floors, so a wall that shows a verified holder's PFP is a real flex. These
are players' own assets (user content); in-game NPC or house art never uses these names — parody only.

Empty walls: [Proposal, FE art] house prints that fit CT — "framed first PnL card", "framed liquidation notice",
"WAGMI poster (ironic)". Not built; needs art.

## Sources

- [Helius DAS getAssetsByOwner](https://www.helius.dev/docs/api-reference/das/getassetsbyowner) (opened 2026-10-08).
- Magic Eden stats API, e.g. `https://api-mainnet.magiceden.dev/v2/collections/mad_lads/stats` (opened 2026-10-08;
  floors change by the minute).
- Unity `Texture2D.LoadImage` decodes PNG/JPEG [verified only by compiling against Unity 6000.3.25f1 reference
  assemblies; decode behaviour on device is unverified].
