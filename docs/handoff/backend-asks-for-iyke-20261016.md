CT Life backend asks (from Dominion's frontend, 16 Oct). Checked against ct-life-backend main 2622b5c.

The Solana Duo phone was rebuilt and is on dev/main (0f48a349). These are the backend pieces it needs. #1 is a bug; #2–#5 are new data so the phone stops showing gaps.

1) MUST: DMs are never pushed live (bug)
- Where: server/application/social-graph.ts → sendDm(). It inserts into dm_messages and returns, but never publishes, so the recipient never gets the realtime "dm" message. Without it the new message banners and badges never fire.
- Fix: after the transaction commits, publish to the recipient on the city room bus. Remove the `void ctx` line:
  import { publishToUser } from './realtime-bus.js';
  await publishToUser(ctx, toUserId, 'dm', { messageId, fromUserId, text, at: Date.parse(at) });
- The payload must match contract/realtime/messages.ts DmMessage (at = epoch ms number). 'dm' is already in UserMessageType and the city room already forwards it; the client already listens.
- Test: user A POSTs /v1/dms/:B while B is in the city room → B receives exactly one 'dm'. A blocked pair → nothing is sent.

2) Loan repay (and due date, if loans have one)
- Today: debtCents only, in the core profile/balances response. No way to repay from the app and no due date.
- Add POST /v1/bank/repay { amountCents, idempotencyKey } → moves cash to debt, returns { cashCents, debtCents }. Errors: insufficient_cash, nothing_to_repay.
- If debt has a deadline in the game design, add debtDueAt (ISO) next to debtCents. If it doesn't, skip that and tell me.

3) Cash history (the Wallet's "Latest transactions")
- Add GET /v1/ledger?before=<cursor>&limit=30 → { items: [{ id, kind, amountCents (signed), label, at }], next }.
- kind values, for example: job_pay, gig_pay, rent, topup, trade_open, trade_close, tip_sent, tip_received, business_collect, repay. Use whatever already writes cash.

4) Perp positions: live PnL
- In the open-positions response, add markPriceCents and unrealizedPnlCents per position, computed server-side from the same price feed as liquidations, so client and server never disagree.

5) Prediction markets list (only if predictions are meant to be markets)
- Today only POST /v1/predictions and GET /v1/predictions (my own bets) exist. The Predict app wants: GET /v1/predictions/markets → [{ marketId, question, yesPct, noPct, volumeCents, closesAt, status }].
- If predictions are fixed-odds bets by design, skip this and tell me.

Not needed: GET /v1/dms already works as the inbox on main (your 03b650d). My local copy was just old.

Optional, if cheap: publishToUser 'toast' for "rent due tomorrow" and "job shift ready", so the phone can show those as notifications too.

When done: reply with the commit hash and whether staging is redeployed. I'll test on the Seeker.
