-- CardVault ledger legacy backfill (Gate 0 / T2b)
--
-- Heals "LedgerEntries" rows written by code that predates the Gate 0 ledger contract:
--   * T1: a hold release/expiry is a negative AuthorizationHold shadow entry, not a Reversal.
--   * T2: credit types (Refund, Reversal, Chargeback) are stored negative.
--   * T3: the original entry of an installment plan is parked as DeferredPrincipal, not Clearing.
--
-- Runs as a single DO block, so it is one transaction: either every rule applies or nothing
-- changes. Each rule is idempotent (a healed row no longer matches its predicate), so running
-- the script twice is safe; the second run reports 0 rows for every rule.
--
-- Rule order matters: rule 1 (hold releases) must run before rule 2 (credit signs). A legacy
-- release is typed Reversal, and rule 2 would otherwise flip it as a credit instead of letting
-- rule 1 turn it into a hold shadow.
--
-- LedgerEntryType numeric values (CardVault.Domain.LedgerEntryType):
--   1 Purchase, 2 Payment, 3 Fee, 4 Interest, 5 Adjustment, 6 Refund, 7 Reversal,
--   8 Chargeback, 9 AuthorizationHold, 10 Clearing, 11 DeferredPrincipal, 12 Installment
-- InstallmentPlanStatus (CardVault.Infrastructure.Persistence.Billing):
--   1 Active, 2 Completed, 3 Cancelled, 4 Delinquent
--
-- Usage:  psql "<connection string>" -f cardvault-ledger-legacy-backfill.sql
-- Output: one NOTICE per rule, "backfill <rule>: <n> row(s)". Nothing else is printed.
-- See docs/runbooks/cardvault-ledger-legacy-backfill.md for context and verification queries.

DO $$
DECLARE
    hold_releases       integer;
    credit_signs        integer;
    deferred_principals integer;
BEGIN
    -- Rule 1: legacy hold releases. Written as Reversal by HoldService ("AUTH RELEASE ...") and
    -- HoldMaintenanceService ("HOLD EXPIRED ..."), sometimes negative, sometimes flipped positive
    -- by the old LedgerService. Under T1 a release is a negative AuthorizationHold shadow that
    -- nets the positive placement to zero and never touches the posted balance.
    UPDATE "LedgerEntries"
       SET "Type"   = 9,                 -- AuthorizationHold
           "Amount" = -ABS("Amount")
     WHERE "Type" = 7                    -- Reversal
       AND ("Description" LIKE 'HOLD EXPIRED %' OR "Description" LIKE 'AUTH RELEASE %');
    GET DIAGNOSTICS hold_releases = ROW_COUNT;
    RAISE NOTICE 'backfill hold_releases: % row(s)', hold_releases;

    -- Rule 2: credit types stored positive. The old LedgerService applied Math.Abs to every
    -- amount, so refunds, reversals and chargebacks were booked as debits. Payments were always
    -- negative and are not touched.
    UPDATE "LedgerEntries"
       SET "Amount" = -"Amount"
     WHERE "Type" IN (6, 7, 8)           -- Refund, Reversal, Chargeback
       AND "Amount" > 0;
    GET DIAGNOSTICS credit_signs = ROW_COUNT;
    RAISE NOTICE 'backfill credit_signs: % row(s)', credit_signs;

    -- Rule 3: in-flight installment plans created before T3 left their original entry typed
    -- Clearing, so it stayed in the purchase base while each installment was billed again. The
    -- original becomes positive DeferredPrincipal (same amount: the exposure does not move).
    -- Only Active plans qualify; completed or cancelled plans are history.
    UPDATE "LedgerEntries" AS le
       SET "Type"   = 11,                -- DeferredPrincipal
           "Amount" = ABS(le."Amount")
      FROM "InstallmentPlans" AS p
     WHERE p."OriginalLedgerEntryId" = le."Id"
       AND p."Status" = 1                -- Active
       AND le."Type" = 10;               -- Clearing
    GET DIAGNOSTICS deferred_principals = ROW_COUNT;
    RAISE NOTICE 'backfill deferred_principals: % row(s)', deferred_principals;
END
$$;
