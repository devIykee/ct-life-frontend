# Custom-avatar credits — proposal, not a live feature

2026-10-05. User suggested charging tokens for costly personal 3D creation. No currency, retail price, provider, or backend endpoint has been agreed. Existing ORE/SKR features remain separate.

## Recommended product behavior

- Standard authored characters and their supported customization remain selectable without a generation charge.
- Personal 3D generation is a separate optional service. Show a server quote before reserving credits; never infer a token amount from a dollar price on the client.
- One quote purchases one explicitly described generation attempt, including the promised processing stages. State the retry/refund policy before confirmation.
- Keep the current avatar while processing. Only equip a new avatar after its preview and runtime checks pass and the player selects Use avatar.
- Do not expose a working-looking purchase/generation button until the provider permission, backend contract, and payment verification exist.

## Verified costs and blockers

[Meshy API pricing](https://docs.meshy.ai/en/api/pricing), opened 2026-10-05, lists Image-to-3D with 2K textures at 30 provider credits for meshy-7.1/meshy-6. Rigging is 5 credits; remeshing is 5. Thus **40 provider credits** is the arithmetic budget for those three successful requests, before retries, any separately requested animation, storage and processing. These are Meshy credits, not CT Life credits, ORE or SKR. The page flags legacy model retirements; pin a supported model version instead of relying on `latest`.

**USD per provider credit is not verified.** The public pricing page did not expose a usable current API dollar conversion in the retrieved content. A defensible retail token price needs the actual API purchase quote and observed usable-output rate.

**Selfie input is not cleared for this provider.** Meshy's [Terms of Service](https://www.meshy.ai/terms-of-use), updated September 19, 2026, section 2.2 prohibits personally identifiable customer input. Section 2.9 permits non-Enterprise training use unless separately agreed. [Inference] A real-person likeness workflow therefore needs explicit suitable provider terms before we send photos; ordinary paid-plan ownership language is insufficient. No photos have been uploaded. An original fictional-character workflow can be evaluated separately under the applicable service agreement.

[Auto-rigging documentation](https://docs.meshy.ai/en/api/rigging) requires a textured, clearly structured humanoid; it does not promise arbitrary characters will rig well. Its input-task route rejects over 300,000 faces. Generated output still requires face, hands, joints, materials, LOD and mobile animation review.

[API asset retention](https://docs.meshy.ai/en/api/asset-retention) is three days for non-Enterprise accounts. Copy accepted results to owned storage before expiry; signed provider URLs are not permanent avatar IDs.

## Proposed FE/BE boundary

These names are a proposal for devIykee to review, not claimed existing routes.

- Quote: `quoteId`, `expiresAt`, `priceMinor`, `currencyId`, included stages, retry/refund policy version. Amounts are integer units; the server owns conversion and expiry.
- Start: `quoteId`, authorized source-asset reference, consent-policy version, idempotency key. Return stable `jobId`; repeated requests do not create extra billable work.
- Job states: `reserved → generating → processing → quality_review → ready`; terminal `failed`, `cancelled`, `refunded`. Payment reservation and job state are separate records.
- FE renders progress from server state and resumes by job ID after restart. A provider timeout is not proof of failure; reconcile before retrying.
- BE holds provider secrets, validates source permission, enforces per-user/day spend limits, checks webhooks/status, records actual supplier cost, validates files and publishes versioned character manifests.
- Manifest: asset ID/version, platform bundle hash, license/provenance, skeleton/animation compatibility, bounds, LOD triangles/materials/textures, preview URL and approved look IDs.
- Charge settlement/refund happens from server-verified job outcomes; a client success screen is never payment evidence.
- SKR payment support is a possible purchase rail; it requires a server quote and verified transaction contract. Do not invent a new CT tradable token or use ORE mining/rewards as an implicit purchase balance.

## Acceptance before launch

- Provider agreement explicitly permits intended real-person inputs, end-user use, required privacy/retention, and distribution.
- Measure cost per **accepted** avatar across varied adult faces, body types, skin tones and outfits, including failed attempts and rework.
- Run the resulting rig in the existing mobile animation/LOD pipeline on Seeker; a textured static preview is not a completed player character.
- FE and BE replay the same quote expiry, duplicate request, failed provider job, refund and app-restart fixtures before enabling purchases.
