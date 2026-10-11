# client/Experiments~ (not compiled)

Unity ignores folders ending in `~`, so nothing here is compiled, imported or shipped.
Moved out of `client/Assets` on 2026-10-07 (integration lead) per `docs/AGENTS-BRIEF.md`
("Shipping code only in client/Assets"):

- `Editor/` — 122 one-off editor probes, candidates, reviews, bakes and receipt-driven tools
  (incl. `PlayableVenueIntegration`, `SeatedGoalRefinement`, `SnowWalkImport`, `FrontendReview`).
- `Runtime/App/Review/` — the `review=1` visual-review fixtures (`ReviewTransport`, `FrontendReviewController`,
  `PracticeJobsApp`, presentation/native reviews). The real flow against the backend replaces them.
- `Runtime/App/Spike/`, `Scenes/Spike.unity` — the engine spike.
- `Runtime/Net/Review/`, `Runtime/World/Review/`, `Runtime/UI/Phone/Apps/PerpsReviewProbe.cs`.

Their evidence paths now point at the archive (`../ct-life-evidence/...`, see
`docs/evidence-archive-manifest.txt`). To revive a tool, move it back under `client/Assets/Scripts/Editor`
(and any runtime fixture it needs), then fix it against current APIs; many reference types that no longer exist.
