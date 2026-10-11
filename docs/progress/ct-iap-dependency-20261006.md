# Unity IAP dependency decision — 2026-10-06

[Inference] Recommendation: remove the unused Unity IAP direct dependency from this frontend. Migrating a purchasing implementation is unnecessary because no Unity IAP purchasing implementation or serialized component was found. This resolves a known deprecated dependency; whether it contributes to the Android app closing is unverified and needs phone crash evidence.

## Current upstream evidence

The [official Unity package registry](https://packages.unity.com/com.unity.purchasing), fetched on 2026-10-06 and retained at `/tmp/ct-iap-registry-20261006.json`, reports:

| Version | Published | Status | Minimum Editor |
| --- | --- | --- | --- |
| Installed 4.15.1 | 2026-04-21 | Deprecated; Unity IAP 4 unsupported since 2026-06-08 | 2020.3 |
| Latest 5.4.4 | 2026-10-03 | No deprecation flag | 2022.3 |

The current [Unity IAP migration guide](https://docs.unity.com/en-us/iap/upgrade-to-iap-v5) describes substantive integration work: separate store connection/product/purchase retrieval, StoreController events replacing listener callbacks, pending-order fulfillment/confirmation, updated receipts and entitlements, and consent handling in 5.4. Therefore upgrading solely to clear a package warning would add migration surface without a demonstrated game need.

## Local usage audit

- `Packages/manifest.json` directly listed `com.unity.purchasing: 4.15.1`; the lock graph contains no other package depending on purchasing.
- Searches of project scripts, asmdefs/asmrefs, linker configuration, resources, Android Gradle configuration, and project settings found no Purchasing namespace/API/assembly references. No compiled DLLs under Assets hide a local code dependency.
- An exhaustive scan of serialized Assets against every `.cs.meta` GUID in the installed IAP package found zero references. No product catalog or codeless IAP buttons/listeners were found.
- `Assets/Resources/BillingMode.json` contains `{"androidStore":"GooglePlay"}`. Installed `StandardPurchasingModule.cs` reads this as store selection if the package is initialized; project code does not read it. It is not a live payment integration. The file and its meta are retained unchanged under the authorized narrow scope.
- The package contains automatic runtime registration hooks, so absence of explicit project references is not a claim that the installed package executes no code. Its codeless initialization requires an enabled, nonempty product catalog, which the project does not have.
- `com.unity.services.core` is also required by installed `com.unity.services.analytics`; do not manually remove shared transitive packages.

## Existing payment behavior

The local contract at `docs/spec/contract.md:270` specifies Solana payment intents, USDC/SOL/SKR/ORE currencies, MWA signing and server confirmation. `MwaWallet.SignAndSendBase64` uses the Solana wallet adapter. Top-up is currently a ComingApp/feature-gated entry in PhoneCatalog. These are separate from Unity store IAP. Removing IAP does not implement, migrate, enable or validate those planned payment flows. Financial behavior and all contract/backend files remain unchanged.

## Authorized change and rollback

After the root confirmed the GUI had exited, only this manifest line was removed:

```diff
-    "com.unity.purchasing": "4.15.1",
```

Backup: `/tmp/ct-unused-iap-removal-20261006-162207/`, including before/after manifest, original lock, and receipt. Before manifest SHA256 `0d2c0486baffb6565bf7559a5bcef67cd26ed828a767c6b933cbb703b5a1d5c4`; after `2ff6ab43a9450979c48f71d3fe5f5e8718eed9087497c8c05b4d875bacda3496`. The lock was deliberately untouched for Unity to resolve. [Unity's package removal documentation](https://docs.unity3d.com/6000.3/Documentation/Manual/upm-ui-remove.html) describes removing the direct manifest dependency and retaining a package if another dependency requires it.

Next root-owned steps: open Unity once, let its resolver update the lock, inspect the exact dependency diff, check native compilation and that the specific IAP warning is absent, then build/launch Android with logs. Do not conflate successful editor/package resolution with device crash resolution. No payments are needed for these checks.

If a future distribution target requires store purchases, treat adding IAP 5 as a separate scoped payment integration: confirm that channel's requirements, define products/receipts and backend fulfillment contract, pin a then-current supported version, implement callbacks and idempotent fulfillment, and test sandbox restore/cancel/pending/replay behavior. That feature is neither requested nor implemented by this cleanup.
