READY TO COMMIT: `client/Assets/Scripts/Net/Auth/Onboarding.cs`, `client/Assets/Scripts/Net/Auth/OnboardingTransactionStore.cs` (+ `.meta`), `client/Assets/Scripts/Net/Rest/Api.cs`, `client/Assets/Tests/EditMode/OnboardingTransactionTests.cs` (+ `.meta`) — retain onboarding request keys and exact birth receipts across retries.

# Onboarding contract and transaction helpers — 2026-10-11

User requested character selections before an automatic lottery and card reveal. The imported contract and local backend (`eefa667`) still require the lottery API write before character creation. Collecting the draft first is compatible; reversing API writes is not.

Implemented frontend Net helpers, with no backend or UI changes in this lane:

- `OnboardingTransactionStore(accountId)`, environment/account scoped. `LotteryKey()` flushes the retained key before returning it. `RecordLottery(response)` flushes the exact birth outcome, cash, debt and home. `Receipt` returns a defensive copy.
- `RecordRolledWithoutReceipt()` and `LotteryRecorded` represent a verified completed roll without claiming its unknown outcome. A prior exact receipt survives subsequent unknown-outcome reconciliation.
- `CharacterKey(alias, look, traits, dream, tribe)` retains a key for the exact required-age-confirmed request body. Only its SHA-256 signature is stored, not the draft or reusable age consent. Changed request content rotates the key; callers must reconcile uncertain writes before allowing edits.
- `ClearCharacterRequest()` retains the birth receipt. `Clear()` is an explicit entire-journal reset, used in isolated tests; normal completion should use the former.
- `Api.CreateCharacter` and `Api.RollLottery` append optional `idempotencyKey` after `ct`, preserving existing call compatibility. API transmission and request-signature hashing share the same body serializer.
- `Onboarding.CharacterCreationRecorded(freshMe)` requires the actual character, not balances or flags. Intended for recovery when creation committed but its response was lost.

Recovery limitation: local backend `/me` reports `parents_basement` before character creation regardless of origin, and does not return the original roll. Its replay cache retains receipts for 24 hours. Never guess a birth outcome from this home or current balances. A saved receipt or same-key replay provides the exact result; after that, show an unknown origin rather than fabricate one. Backend's character home mapping also differs from configured birth-home IDs; the birth card should use the roll receipt and current access should use the character response.

Validation: `git diff --check` passed during implementation. New EditMode tests cover restart retention, account/environment isolation, immutable receipt snapshots, unknown-outcome reconciliation, rejected malformed results, field-by-field request key rotation, non-persisted age consent, character response-loss reconciliation, and API key/body forwarding. Unity tests/build/device checks were not run by this read/write lane; coordinator owns them.

Coordinator test filter: `CTLife.Tests.OnboardingTransactionTests` plus existing `OnboardingTests`, `OnboardingUiTests`, and `OnboardingR2Tests` after the UI changes land.

## Device regression: birth debt was announced as a rent penalty

Coordinator's Seeker test found `PlayerProfile.ApplyLottery` publishing a profile update that the already-subscribed City Life rent watcher treated as a Sunday Unlock debt increase. `CityLifeHub.CheckRent` now ignores profiles without an actual character and resets its prior rent/debt baseline. The first character observation starts rent monitoring; later debt increases keep the existing notification behavior.

Added `CTLife.CityLife.Tests.CityLifeRentObservationTests` (two cases). These invoke the real profile callback through the signal bus and real `PlayerProfile.ApplyLottery`, while isolating the hub from its unrelated HTTP/background loop. Assertions cover silent birth debt, silent first-character observation, later debt notification, and clearing an old baseline when entering a characterless state. No test/build/device commands were run by this lane; coordinator owns verification.

Additional READY TO COMMIT paths: `client/Assets/Scripts/Features/CityLife/Net/CityLifeHub.cs`, `client/Assets/Scripts/Features/CityLife/Tests/CityLifeRentObservationTests.cs` (+ `.meta`).
