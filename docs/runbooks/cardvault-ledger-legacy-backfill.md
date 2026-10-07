# CardVault ledger legacy backfill (Gate 0 / T2b)

**Audience:** whoever keeps a CardVault database that was written to before Gate 0 landed.
**Applies to:** `LedgerEntries` and `InstallmentPlans` in the CardVault PostgreSQL database.
**Script:** [`sql/cardvault-ledger-legacy-backfill.sql`](sql/cardvault-ledger-legacy-backfill.sql).

## When this is relevant

Only for databases that hold ledger rows produced by CardVault builds **before** the Gate 0
ledger fixes (T1 hold shadows, T2 sign contract, T3 deferred principal). No production database
exists; in practice this means long-lived developer databases. A database created after Gate 0,
or recreated from scratch, has nothing to heal and the script reports `0 row(s)` for every rule.

The fix is a documented one-off script rather than an EF Core migration on purpose:
`MigrationBaselineAdopter` refuses to adopt an `EnsureCreated()` database once the migrations
assembly holds more than the single `InitialBaseline` (see `cardvault-migration-baseline.md`),
and a data-only migration would have forced every developer database through that path.

## Legacy shapes and what the script does

`LedgerEntryType` numeric values: 1 Purchase, 2 Payment, 3 Fee, 4 Interest, 5 Adjustment,
6 Refund, 7 Reversal, 8 Chargeback, 9 AuthorizationHold, 10 Clearing, 11 DeferredPrincipal,
12 Installment. `InstallmentPlanStatus`: 1 Active, 2 Completed, 3 Cancelled, 4 Delinquent.

| Rule | Legacy shape | Why it is wrong today | Change |
|---|---|---|---|
| 1 `hold_releases` | `Type = 7` (Reversal) and `Description LIKE 'HOLD EXPIRED %'` or `'AUTH RELEASE %'` | A release was balance-affecting: it credited the customer for a hold that was never posted debt. Under T1 a release is a negative `AuthorizationHold` shadow that nets the placement to zero. | `Type = 9`, `Amount = -ABS(Amount)` |
| 2 `credit_signs` | `Type IN (6, 7, 8)` and `Amount > 0` | The old `LedgerService` applied `Math.Abs` to every amount, booking refunds, reversals and chargebacks as debits. | `Amount = -Amount` |
| 3 `deferred_principals` | `InstallmentPlans.Status = 1` whose `OriginalLedgerEntryId` row is still `Type = 10` (Clearing) | Before T3 the original stayed in the purchase base while each installment was billed again. Under T3 it is parked as positive `DeferredPrincipal`, outside statement balances but inside the exposure. | `Type = 11`, `Amount = ABS(Amount)` |

Rule order matters. Rule 1 runs before rule 2: a legacy release is typed Reversal, and rule 2
would otherwise flip it as a credit instead of letting rule 1 convert it into a hold shadow.
Rule 3 is independent of the other two.

Everything runs inside one `DO` block, so it is a single transaction: either every rule applies
or nothing changes. Each rule is idempotent because a healed row no longer matches its predicate.

Payments (`Type = 2`) were always stored negative and are not touched. `Adjustment` (`Type = 5`)
is a signed type; its only writer, `DisputeService.ResolveAsync` (chargeback lost, re-debit),
posts `Math.Abs(amount)` as a debit, which is the intended direction, so no backfill is needed.

### What the script does not undo

A legacy plan's original entry may already carry a `StatementId`: the old billing engine invoiced
it in its purchase cycle *and* then billed the installments (the double billing the audit found).
The script reclassifies the row so that no further installment is double-counted and the ledger
invariant (`original = remaining DeferredPrincipal + billed Installment`) holds from now on, but it
does not rewrite closed statements. Treat already-issued statements on such accounts as suspect.

## Code guard

`BillingService.GenerateStatementAsync` applies rule 3 on its own when it meets an active plan
whose original entry is still `Clearing` or `Purchase`: the entry is reclassified in flight,
persisted, and a Warning is logged with the plan, account and ledger entry ids. The heal is
idempotent, so a database that was never backfilled still produces correct statements, one plan at
a time. A plan whose original entry is missing or has any other type is **not** billed; an Error is
logged and its schedule stays `Pending` until a human reconciles it. Rules 1 and 2 have no code
guard: the services that wrote those shapes no longer exist, and the script is the remedy.

## How to run

```powershell
psql "Host=localhost;Port=5432;Database=cardvault;Username=postgres" `
  -f docs/runbooks/sql/cardvault-ledger-legacy-backfill.sql
```

Or with a URI: `psql postgresql://postgres@localhost:5432/cardvault -f docs/runbooks/sql/cardvault-ledger-legacy-backfill.sql`.

Expected output, one line per rule:

```
NOTICE:  backfill hold_releases: 2 row(s)
NOTICE:  backfill credit_signs: 3 row(s)
NOTICE:  backfill deferred_principals: 1 row(s)
DO
```

Run it a second time: every count must be `0`. Take a backup first if the database matters to you;
the script is reversible only through that backup.

## How to verify

Before and after, these queries should move from non-zero to zero:

```sql
-- Rule 1 candidates
SELECT COUNT(*) FROM "LedgerEntries"
 WHERE "Type" = 7 AND ("Description" LIKE 'HOLD EXPIRED %' OR "Description" LIKE 'AUTH RELEASE %');

-- Rule 2 candidates
SELECT COUNT(*) FROM "LedgerEntries" WHERE "Type" IN (6, 7, 8) AND "Amount" > 0;

-- Rule 3 candidates
SELECT COUNT(*) FROM "LedgerEntries" le
  JOIN "InstallmentPlans" p ON p."OriginalLedgerEntryId" = le."Id"
 WHERE p."Status" = 1 AND le."Type" = 10;
```

And these invariants should hold afterwards:

```sql
-- Hold shadows of released holds net to zero per account
SELECT "AccountId", SUM("Amount") FROM "LedgerEntries" WHERE "Type" = 9
 GROUP BY "AccountId" HAVING SUM("Amount") <> 0;
-- (rows here are accounts with holds still open, which is legitimate; released ones must not appear)

-- Deferred principal is never negative per account
SELECT "AccountId", SUM("Amount") FROM "LedgerEntries" WHERE "Type" = 11
 GROUP BY "AccountId" HAVING SUM("Amount") < 0;
```

`CardVault.IntegrationTests/Ledger/LegacyLedgerBackfillIntegrationTests.cs` seeds all three shapes
on a fresh PostgreSQL database, runs this exact file through Npgsql, asserts the per-rule counts,
runs it again expecting zeros, and checks that `AvailableCreditService` reports a posted balance
made of real debt only. The project file copies the script next to the test assembly, so the test
always exercises the committed version.
