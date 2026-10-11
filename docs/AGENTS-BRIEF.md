# Brief for every agent working on CT Life

Read this first, then the spec in `docs/spec/build-spec.md` and `docs/spec/contract.md`
(exported from the Claude Doc "CT Life — Build Spec"). The spec is the source of truth.

## What we are building
A 3D multiplayer Crypto Twitter life-sim, native Android (Unity 6.3 LTS) for the Solana Seeker
phone. Backend: Node 22+ / Fastify 5 / Colyseus 0.18 / BullMQ / Postgres / Redis. On-chain:
Anchor `ct_escrow`, USDC/SOL/SKR/ORE payments, ORE staking. Voice/video: LiveKit (self-hosted
`livekit-server --dev` in Docker for development; no cloud account yet).

Build order is six phases (spec, "Phases"). Phase 1 = City and identity, Phase 2 = Money and
markets (incl. ORE), Phase 3 = Voice and calls, Phase 4 = City life, Phase 5 = Projects and ads,
Phase 6 = Competition. Every phase must end in something that runs. Do NOT reduce scope to an
"MVP"; build the phase completely, then continue to the next.

## Art direction (updated 2026-10-05 from the user's references: The Sims 4)
- **Sims-style stylized realism**, not chibi: real human proportions, soft stylized faces,
  warm saturated palette, lush trees (incl. pink blossom trees), clean brick/stucco/glass
  buildings, golden-hour sun, soft shadows, a high 3/4 "tilt" camera over a living city.
- CT parody on top: billboards, neon terminals, trading screens, Dubai-marina glass, a farm.
- Must still hit 60 fps on the Seeker (Mali-G615 MC2): budgets in spec "Performance budgets".
  Bake lighting, LODs, ASTC textures, one material per character, atlases where possible.

## Licences (hard rules)
- Use **CC0 first** (Kenney, Quaternius, KayKit, Poly Haven, ambientCG, MakeHuman/MPFB assets,
  freesound CC0, Kenney audio). CC-BY only if nothing CC0 fits, and record attribution.
- **Banned:** Synty (EULA bans blockchain projects), Unity Asset Store assets (can't be used in
  "digital representation of value"), ActorCore (no IAP use), Ready Player Me (shut down),
  anything needing a login we don't have (Mixamo needs an Adobe login — do not use), Udio.
- Every downloaded file gets a row in `art/LICENSES.md` or `audio/LICENSES.md`:
  name, source URL, licence, author, date downloaded, where it is used.

## Working rules
- Verify every package/tool version on its live registry or release page before using it.
  Never trust memory for APIs. Label anything unverified `[Unverified]` in your notes.
- Stay inside your folder(s) (see README.md ownership table). Read other folders freely.
  Never edit another agent's folder; write a request in `docs/progress/requests.md` instead.
- Do not `git commit` or push. Do not deploy anything to a public host. Do not create accounts
  or sign in anywhere. Do not spend money. Devnet/localnet only for Solana writes.
- Keep secrets out of the repo (`.env` is gitignored; commit `.env.example` only).
- Log progress in `docs/progress/<your-agent-name>.md`: what's done, what's next, blockers,
  exact commands to run/verify. Update it as you go, not only at the end.
- The Seeker phone is connected over adb (serial SM02G40619141343). Only the Unity agent
  installs builds on it.
- Report outcomes faithfully. If something fails, say so with the error.
