# Read-only connected wallet balances — 2026-10-12

Implementation and automated checks complete. The coordinator's final EditMode run passed 31/31 tests (13 balance, 4 Wallet app, 14 Home UI). The final PlayMode run passed 11 tests, including the actual public-RPC Unity transport check; one pre-existing opt-in eye screenshot test was ignored because its output directory was unset. No tests failed. Android build and device UI smoke checks completed with the coordinator; actual connected-wallet acceptance remains pending because the retained Seeker session is a developer login, not SIWS. No Unity, ADB, build or Git mutation was performed by this lane.

## Implemented

- `client/Assets/Scripts/Net/Wallet/WalletBalanceService.cs`: one shared authenticated-SIWS wallet snapshot, mainnet by default, explicit network, partial failure/loading state, stale last observations, refresh coalescing/cooldowns, identity/config invalidation, resume refresh.
- `WalletBalanceRpc.cs`: only three allowed read methods, credential-free official public endpoint, bounded response/timeout and error codes. No signing or money movement.
- `WalletBalanceState.cs`: exact amount parsing, all token accounts summed, owner/mint/program/decimals validation, nullable unknown amounts, tiny nonzero formatting.
- `GameBootstrap.cs`: service registration and disposal of a previous service during a repeated bootstrap. Lobby/phone UI is owned by the coordinator and phone agent.
- `WalletBalanceTests.cs`: thirteen focused offline tests, including transport cancellation, observation ageing/config invalidation and unexpected refresh setup failure. Research and the independently observed SKR precision mismatch are documented separately.
- `WalletBalanceRpcTests.cs`: opt-in actual UnityWebRequest verification against a public mainnet system account. This does not establish connected-player acceptance.

Lifecycle review also added creation of the persistent Runner before subscribing to Android resume, distinguished cancelled ownership from transport timeout/cancellation, and made unexpected setup failures finish with non-refreshing unavailable/stale state.

## UI contract

`Services.Get<WalletBalanceService>()`; subscribe/unsubscribe its `Changed` event and call `await Refresh(force: true)` for a deliberate refresh. Do not dispose the shared service when closing a screen. `.State` exposes `Wallet`, `Cluster`, `Status`, `IsRefreshing`, `Tokens`, and `Token("SOL")`.

`WalletBalanceStatus`: Disconnected, Loading, Ready, Partial, Unavailable. Per-token `.State` is `WalletTokenState`: Loading, Ready, Unavailable. A token also has `Symbol`, `Mint`, nullable decimal `Amount`, exact base-unit `RawAmount`, actual mint `Decimals`, `IsStale`, nullable `UpdatedAtUtc`, `ErrorCode`, and `FormatAmount(maxDecimals: 4)`.

Formatting returns null when no balance has been observed and a `<0.0001` style value for a tiny positive holding. Keep Mainnet visible, use a loading/unknown presentation for null, and label retained stale values. These values are never game Cash or USD portfolio totals.

## Verification coverage and device acceptance

1. EditMode filter `CTLife.Tests.WalletBalanceTests`: u64 precision/real zero; multiple/empty token accounts; malformed/foreign/duplicate account rejection; SKR on-chain 6 vs config5; partial failures; stale values/recovery; coalescing and old-account late response; cooldown/mint cache; devnet absent mints; tiny nonzero/disconnected behavior; transport cancellation; age/config invalidation; unexpected setup failure.
2. Bootstrap/lobby/phone compile together, open both UI surfaces, verify they consume the same snapshot without duplicated requests.
3. On the signed-in Seeker, compare displayed SOL/USDC/SKR/ORE with the same wallet's mainnet holdings. Check offline refresh retains amounts only as stale, never resets to zero. Check logout clears the display immediately.
4. Confirm no MWA prompt or transaction appears from balance viewing/refresh. Current staging payments remain separate from displayed mainnet holdings.
5. Optional PlayMode filter `CTLife.Tests.WalletBalanceRpcTests` with `CTLIFE_WALLET_RPC_REVIEW=1` performs exactly three public read-only RPC calls through the production Unity transport. Set `CTLIFE_WALLET_RPC_EVIDENCE` to an external evidence directory to write the receipt. The test skips without the opt-in environment variable.

Read-only HTTP verification succeeded for seven calls (native SOL, three mint metadata reads, three owner/mint reads); evidence is `../ct-life-evidence/wallet-balances-20261012/public-rpc-verification.json`. This used a public system account, never the player's wallet.

## Final automated receipts

- `../ct-life-evidence/lobby-duo-20261012/final-editmode.xml`: 31 passed, zero failed or skipped. This includes all thirteen wallet service cases and four actual Wallet app tests.
- `../ct-life-evidence/lobby-duo-20261012/final-playmode.xml`: 11 passed, zero failed, one intentionally ignored old eye screenshot opt-in. `WalletBalanceRpcTests.ReadsPublicMainnetAccountThroughUnityTransport` passed.
- `../ct-life-evidence/lobby-duo-20261012/unity-public-rpc.json`: actual production UnityWebRequest made three read-only mainnet calls successfully at 2026-10-12T01:50:39Z. The public system-account fixture is not the player's connected wallet.
- `../ct-life-evidence/lobby-duo-20261012/lobby-final/receipt.json`: 28 screenshot cases passed, including compact large lobby amounts, full-precision Wallet amounts, unavailable/disconnected/stale states and shared-service restoration. Local fixtures are visibly marked; no backend writes or wallet signing occurred.

The final read-only audit found all release client files identical to the tested build clone, complete asset metadata, no detected secret patterns and no oversized files. Device UI acceptance is recorded in the lobby progress log; comparison with the player’s signed-in wallet remains pending.

Known infrastructure limit: official public RPC is rate-limited and has no production availability commitment. A launch-grade backend/provider endpoint remains a follow-up; see research. Backend `/ore/me` currently returns stub zeros, so it is deliberately not used here.
