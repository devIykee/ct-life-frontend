# Connected wallet holdings — 2026-10-12

The signed-in lobby/phone needs the connected wallet's actual SOL, USDC, SKR and ORE holdings. These are separate from fictional game Cash. The coordinator chose **mainnet holdings**, explicitly labelled Mainnet, while the current game environment can continue using devnet for payment testing.

## Available project data

- `Session.Wallet` is the authenticated wallet address. The app service accepts it only for a signed-in `siws` session; developer-auth fixtures do not impersonate a connected wallet.
- `GET /me` exposes the wallet address and game `cashCents`, but no native/SPL wallet balances.
- `GET /ore/me` has a balance field, but the current backend source `server/application/refinery.ts:readOreMe` returns literal `balance: '0', staked: '0', stOre: '0'`. That response cannot establish the wallet's ORE holdings. The backend was inspected read-only.
- `contract/config/economy.json` identifies the mainnet token mints and devnet USDC mint. It has no verified devnet SKR/ORE mints. The snapshot contract is unchanged.

## Primary sources and blocker checks

- Solana [`getBalance`](https://solana.com/docs/rpc/http/getbalance) returns the native account balance in lamports. [`getTokenAccountsByOwner`](https://solana.com/docs/rpc/http/gettokenaccountsbyowner) returns token accounts for an owner/mint; sum raw integer amounts across all matching accounts, not a single assumed associated token account. Both support a commitment parameter.
- Solana [cluster documentation](https://solana.com/docs/references/clusters) distinguishes mainnet from valueless devnet tokens and lists `https://api.mainnet.solana.com` / `https://api.devnet.solana.com`. It explicitly says public RPC endpoints are rate-limited and not intended as production infrastructure. **Launch reliability still needs a dedicated provider or a backend read endpoint; this credential-free integration does not establish a production RPC SLA.**
- Circle's [official address list](https://developers.circle.com/stablecoins/usdc-contract-addresses) confirms mainnet USDC `EPjFWdd5AufqSSqeM2qN1xzybapC8G4wEGGkZwyTDt1v` and devnet `4zMMC9srt5Ri5X14GAgXhaHii3GnPAEERYPJgZJDncDU`.
- Solana Mobile's [SKR page](https://solanamobile.com/skr) confirms `SKRbvo6Gf7GondiT3BbTfuRDPqLWei4j2Qy2NPGZhW3`.
- The ORE team's [mint constants](https://github.com/regolith-labs/ore-mint/blob/master/api/src/consts.rs) identify `oreoU2P8bN6jkk3jbaiVxYnG1dCXcYxwhwyK9jSybcp` with 11 decimals.
- Current Unity [UnityWebRequest](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Networking.UnityWebRequest.SendWebRequest.html) and [DownloadHandlerScript](https://docs.unity3d.com/6000.3/Documentation/ScriptReference/Networking.DownloadHandlerScript.html) APIs support the existing project's HTTP approach and bounded response handling. No package was added.

## Found mismatch: SKR decimals

A read-only `getAccountInfo` query to mainnet returned initialized standard-token-program mint accounts with USDC **6**, SKR **6**, ORE **11** decimals. The project contract says SKR **5**. Scaling the actual raw amount by that value would overstate SKR holdings tenfold.

The balance service obtains decimals from the mint account, validates every returned token account against its owner, mint, token program and decimals, and ignores the inconsistent display precision in config. The contract correction request is in `docs/fe-contract-proposals/skr-token-decimals-20261012.md`. No payment code or contract snapshot was changed by this task.

Read evidence is outside the repository: `../ct-life-evidence/wallet-balances-20261012/public-rpc-verification.json`. The probe uses the public system-program address, not a person's wallet; it sends only getBalance/getAccountInfo/getTokenAccountsByOwner reads and no signing requests.

## State and safety of representation

One shared service supplies the lobby and phone. Its mint selection comes from the current config, its network is explicit, and values are exact integer base units before decimal conversion. Empty successful token-account lists are genuine zero; missing fields, malformed data, mismatched identities and RPC failures are unavailable.

The service coalesces concurrent refreshes, normally refreshes at most once per 30 seconds, applies a five-second manual-refresh cooldown, caches mint metadata for ten minutes and caps each reply at 512 KiB with a 12-second timeout. A failed or active refresh can preserve the last observation from the same wallet/mint, labelled stale. Account/config changes cancel pending reads and clear the previous identity's values. No wallet prompt, transaction submission, credential, backend mutation, token pricing, USD net-worth estimate or fabricated balance is involved.

These are wallet holdings, not a guarantee of immediately transferable assets. They exclude native staking accounts, locked SKR/ORE positions and other mints; wrapped SOL is not folded into the native SOL figure. Token-2022 account parsing is supported, but the four configured assets verified in this pass use the standard token program or native SOL.
