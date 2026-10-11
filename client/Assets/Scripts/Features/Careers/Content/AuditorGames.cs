using System.Collections.Generic;
using System.Linq;

namespace CTLife.Careers
{
    /// <summary>
    /// Smart Contract Auditor on Audit Row. Anchors: the Solana program bug classes in coral-xyz/sealevel-attacks
    /// (signer authorization, owner/data matching, type cosplay, re-initialization, arbitrary CPI, bump
    /// canonicalization, closing accounts); impact-based severity (Immunefi classification v2.3); token risk checks
    /// (mint/freeze authority, LP, holder concentration, mutable metadata, creator history). Code is pseudo-Anchor
    /// for a fictional "froge_vault" program, trimmed to phone width.
    /// </summary>
    internal static class AuditorGames
    {
        public static IEnumerable<MiniGameSpec> Specs()
        {
            yield return new MiniGameSpec
            {
                Id = "auditor_report", CareerId = "smart_contract_auditor", InContract = true, Engine = MiniGameEngine.Sort,
                Title = "Severity Call", Tagline = "Rate each finding by what it lets an attacker do.",
                Rules = new[]
                {
                    "Critical: user funds stolen or frozen for good, unlimited minting, protocol insolvent.",
                    "High: unclaimed yield or royalties stolen, funds frozen for a while.",
                    "Medium: griefing and failures that cost nothing but time (spam, compute blow-ups).",
                    "Low: the app under-delivers what it promised, nobody loses deposits.",
                },
                ScoringRule = "Right calls minus a third of a point per wrong call, out of the stack.",
                RealTask = "Triaging findings by impact, the way bug-bounty and audit reports grade them.",
                ActionLabel = "Critical|High|Medium|Low",
                DefaultDuration = 50,
                Create = init => new SortRound(new[] { "Critical", "High", "Medium", "Low" }, ContentKit.Deck(new DetRng(init.Seed), Findings, 4, 12), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "auditor_spot_the_bug", CareerId = "smart_contract_auditor", Engine = MiniGameEngine.Flag,
                Title = "Spot the Bug", Tagline = "One review pass. Flag the line that drains the vault.",
                Rules = new[]
                {
                    "Each snippet is part of the froge_vault program. Flag the line that is exploitable.",
                    "Usual suspects: a missing signer, an account nobody checks, init that runs twice, a bump the caller picks, a CPI to any program, lamports drained without closing.",
                    "File the snippet to see the fix.",
                },
                ScoringRule = "Per snippet: bugs found minus clean lines flagged (never below zero).",
                RealTask = "Code review against the common Solana program exploit classes.",
                DefaultDuration = 55,
                Create = init => new FlagRound(ContentKit.Boards(new DetRng(init.Seed), Snippets, 4), init.MaxScore, init.Duration),
            };
            yield return new MiniGameSpec
            {
                Id = "auditor_rug_scanner", CareerId = "smart_contract_auditor", Engine = MiniGameEngine.Flag,
                Title = "Rug Risk Scanner", Tagline = "Read the token report like a pro. Flag the red lines.",
                Rules = new[]
                {
                    "Each report is a fresh coin on Launchpad Tower.",
                    "Flag every line that gives the dev power over your tokens or shows a stacked deck: live mint or freeze authority, unlocked LP, concentrated supply, dev dumping, same-funder launch buys, mutable metadata, a rug history, transfer fees the owner controls.",
                    "Safe lines stay unflagged.",
                },
                ScoringRule = "Per report: risks found minus safe lines flagged (never below zero).",
                RealTask = "Pre-trade token checks with a rug scanner.",
                DefaultDuration = 50,
                Create = init => new FlagRound(RugReports(new DetRng(init.Seed), 4), init.MaxScore, init.Duration),
            };
        }

        private static readonly SortCard[] Findings =
        {
            ContentKit.Card(0, "withdraw() never checks that the vault owner signed.", "Anyone can pull anyone's deposit: direct theft. Critical."),
            ContentKit.Card(0, "init() can run twice; the second call replaces the vault authority.", "Take over the authority, take the funds. Critical."),
            ContentKit.Card(0, "The mint authority PDA accepts any bump the caller passes.", "Unlimited minting of the points token. Critical."),
            ContentKit.Card(0, "The lending pool reads a price account the caller supplies.", "Fake price, borrow the pool dry: theft and insolvency. Critical."),
            ContentKit.Card(0, "Admin key can close the vault and keep the balance.", "Permanent loss of user funds. Critical."),
            ContentKit.Card(1, "Any staker can claim another staker's pending rewards.", "Theft of unclaimed yield, deposits untouched. High."),
            ContentKit.Card(1, "Admin can pause withdrawals for up to 7 days.", "Temporary freeze of funds. High."),
            ContentKit.Card(1, "Creator royalties can be redirected before they're claimed.", "Theft of unclaimed royalties. High."),
            ContentKit.Card(1, "A crank bug can lock withdrawals until the next epoch.", "Funds frozen for a while, not lost. High."),
            ContentKit.Card(2, "Dust deposits make close_all() exceed the compute limit.", "Griefing: transactions fail, nothing is stolen. Medium."),
            ContentKit.Card(2, "Anyone can front-run the crank so other users' calls fail.", "Griefing with no profit to the attacker. Medium."),
            ContentKit.Card(2, "The fee vault can run dry, so withdrawals fail until someone tops it up.", "Operational failure from missing funds. Medium."),
            ContentKit.Card(2, "An attacker can spam 0-amount orders to clog the order book.", "Griefing that wastes everyone's compute. Medium."),
            ContentKit.Card(3, "Rewards round down: stakers get slightly less than the docs promise.", "Under-delivers on promised returns, no loss. Low."),
            ContentKit.Card(3, "The app shows an APR 0.3% higher than the vault actually pays.", "Promised return not delivered, deposits safe. Low."),
            ContentKit.Card(3, "Referral bonus is never paid to the referrer.", "Missing promised reward, no funds at risk. Low."),
        };

        private static FlagLine Ok(string t) => new FlagLine(t);
        private static FlagLine Bad(string t, string why) => new FlagLine(t, true, why);

        private static readonly FlagBoard[] Snippets =
        {
            new FlagBoard
            {
                Code = true, Title = "Withdraw accounts", Brief = "Find 1 bug.",
                Lines = new[]
                {
                    Ok("#[derive(Accounts)]"),
                    Ok("pub struct Withdraw<'info> {"),
                    Ok("  #[account(mut, has_one = owner)]"),
                    Ok("  pub vault: Account<'info, Vault>,"),
                    Bad("  pub owner: UncheckedAccount<'info>,", "The owner never has to sign. Pass their key, take their funds. Use Signer<'info>."),
                    Ok("  pub system_program: Program<'info, System>,"),
                    Ok("}"),
                },
            },
            new FlagBoard
            {
                Code = true, Title = "Borrow accounts", Brief = "Find 1 bug.",
                Lines = new[]
                {
                    Ok("pub struct Borrow<'info> {"),
                    Ok("  #[account(mut)] pub pool: Account<'info, Pool>,"),
                    Bad("  pub oracle: UncheckedAccount<'info>,", "Not tied to pool.oracle. Pass your own price account. Add address = pool.oracle."),
                    Ok("  pub user: Signer<'info>,"),
                    Ok("}"),
                    Ok("let px = load_price(&ctx.accounts.oracle)?;"),
                    Ok("require!(collateral * px >= amount * LTV, Err::Ltv);"),
                },
            },
            new FlagBoard
            {
                Code = true, Title = "Config init", Brief = "Find 1 bug.",
                Lines = new[]
                {
                    Ok("pub struct InitConfig<'info> {"),
                    Bad("  #[account(mut)] pub config: Account<'info, Config>,", "mut, not init: anyone can call it again and make themselves admin."),
                    Ok("  #[account(mut)] pub payer: Signer<'info>,"),
                    Ok("}"),
                    Ok("cfg.admin = admin;"),
                    Ok("cfg.fee_bps = 100;"),
                },
            },
            new FlagBoard
            {
                Code = true, Title = "Close a position", Brief = "Find 1 bug.",
                Lines = new[]
                {
                    Ok("#[account(mut, has_one = owner)]"),
                    Ok("pub position: Account<'info, Position>,"),
                    Ok("#[account(mut)] pub owner: Signer<'info>,"),
                    Ok("let lamports = position.to_account_info().lamports();"),
                    Ok("**owner.lamports.borrow_mut() += lamports;"),
                    Bad("**position.lamports.borrow_mut() = 0;", "Draining lamports isn't closing: the data survives and can be revived. Use close = owner."),
                },
            },
            new FlagBoard
            {
                Code = true, Title = "Treasury payout", Brief = "Find 1 bug.",
                Lines = new[]
                {
                    Ok("pub fn pay(ctx: Context<Pay>, bump: u8, amt: u64)"),
                    Bad("let pda = create_program_address(&[b\"treasury\", &[bump]])?;", "The caller picks the bump, so non-canonical PDAs pass. Use the stored canonical bump."),
                    Ok("require_keys_eq!(pda, ctx.accounts.treasury.key());"),
                    Ok("let fee = amt.checked_mul(FEE_BPS)?.checked_div(10_000)?;"),
                    Ok("transfer_signed(&ctx, amt.checked_sub(fee)?)?;"),
                },
            },
            new FlagBoard
            {
                Code = true, Title = "Reward payout CPI", Brief = "Find 1 bug.",
                Lines = new[]
                {
                    Ok("pub struct Payout<'info> {"),
                    Ok("  #[account(mut)] pub vault: Account<'info, TokenAccount>,"),
                    Ok("  #[account(mut)] pub user_ata: Account<'info, TokenAccount>,"),
                    Bad("  pub token_program: UncheckedAccount<'info>,", "Any program can be passed and invoked with the vault's signer seeds. Use Program<'info, Token>."),
                    Ok("  pub authority: Signer<'info>,"),
                    Ok("}"),
                },
            },
            new FlagBoard
            {
                Code = true, Title = "Read config", Brief = "Find 1 bug.",
                Lines = new[]
                {
                    Ok("pub fn set_fee(ctx: Context<SetFee>, bps: u16)"),
                    Bad("let cfg = Config::try_from_slice(&acc.data.borrow())?;", "No owner or discriminator check: a look-alike account passes as Config. Use Account<'info, Config>."),
                    Ok("require_keys_eq!(cfg.admin, ctx.accounts.admin.key());"),
                    Ok("require!(bps <= 1_000, Err::FeeTooHigh);"),
                    Ok("cfg.fee_bps = bps;"),
                },
            },
            new FlagBoard
            {
                Code = true, Title = "Points ledger", Brief = "Find 2 bugs.",
                Lines = new[]
                {
                    Ok("pub fn add_points(ctx: Context<AddPoints>, pts: u64)"),
                    Bad("// admin: AccountInfo, any key accepted", "The admin is never required to sign. Anyone can award points."),
                    Ok("let user = &mut ctx.accounts.user;"),
                    Bad("user.points = user.points + pts;", "Unchecked add wraps around in release builds. Use checked_add."),
                    Ok("user.updated = Clock::get()?.unix_timestamp;"),
                    Ok("emit!(PointsAdded { pts });"),
                },
            },
        };

        // ------------------------------------------------------------ rug reports (procedural)

        private static readonly (string label, string safe, string risky, string why)[] RugFields =
        {
            ("Mint authority", "Revoked", "Active: dev can mint more", "A live mint authority can print supply and dump it."),
            ("Freeze authority", "None", "Active: dev can freeze wallets", "The dev can freeze your tokens so only they can sell."),
            ("Liquidity", "LP burned", "LP unlocked, held by the dev", "Unlocked LP can be pulled: the classic rug."),
            ("Top 10 holders", "17% of supply", "63% of supply", "A few wallets can crash the price at will."),
            ("Dev wallet", "Holds 2%, no sells", "Sold 40% in the first hour", "The dev is already exiting."),
            ("Launch buys", "No same-slot cluster", "14 wallets bought in the launch slot, same funder", "A bundle: one person bought most of the launch."),
            ("Metadata", "Immutable", "Mutable: name and image can change", "The dev can rebrand it into anything."),
            ("Creator history", "2 past coins, both still trading", "9 past coins, all dead within a day", "Serial rugger."),
            ("Transfer fee", "0%", "25%, owner can change it", "A fee the owner controls can trap every sell."),
            ("Socials", "Account active since 2024", "Account made 2 days ago, 40k followers", "Bought followers on a brand-new account."),
            ("Dev stream", "No livestream", "Dev streaming a 'rug at 10M?' chat poll", "A stunt stream where the dev polls chat about rugging. Believe them."),
        };

        public static List<FlagBoard> RugReports(DetRng rng, int count)
        {
            var boards = new List<FlagBoard>();
            var coins = rng.Sample(ContentKit.Coins, count);
            for (int b = 0; b < count; b++)
            {
                var fields = rng.Sample(RugFields, 7);
                int risky = rng.Range(1, 4);
                var riskyIdx = new HashSet<int>(rng.Sample(Enumerable.Range(0, fields.Count).ToList(), risky));
                var lines = fields.Select((f, i) => riskyIdx.Contains(i)
                    ? new FlagLine(f.label + ": " + f.risky, true, f.why)
                    : new FlagLine(f.label + ": " + f.safe, false, null)).ToArray();
                boards.Add(new FlagBoard { Title = "RugRadar report · " + coins[b], Brief = "Find " + risky + " risk" + (risky == 1 ? "" : "s") + ".", Lines = lines });
            }
            return boards;
        }
    }
}
