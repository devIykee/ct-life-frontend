# Correct SKR token precision

Observed 2026-10-12 during the connected-wallet balance work. Frontend proposal only; the imported contract and backend were not edited.

`contract/config/economy.json` currently defines mainnet SKR mint `SKRbvo6Gf7GondiT3BbTfuRDPqLWei4j2Qy2NPGZhW3` with `decimals: 5`. The mint's current `getAccountInfo` JSON-parsed response on `https://api.mainnet.solana.com` reports **decimals: 6**, initialized, owned by `TokenkegQfeZyiNwAJbNbGKPFXCWuBvf9Ss623VQ5DA`. Solana Mobile [confirms that mint](https://solanamobile.com/skr).

Requested backend/contract correction: change SKR decimals to 6, regenerate/publish config as appropriate, and review any conversion paths that scale raw SKR quantities by this field. This note does not claim such a payment path has executed incorrectly; the mismatch itself is verified. Reproduction uses read-only `getAccountInfo` with `{encoding: "jsonParsed", commitment: "confirmed"}` for the mint above. Response evidence: `../ct-life-evidence/wallet-balances-20261012/public-rpc-verification.json`.

The new frontend holdings service independently reads on-chain mint precision, so it does not show balances at the incorrect scale. No payment or signing behavior changed.
