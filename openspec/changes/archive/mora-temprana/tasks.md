# Tasks: Mora Temprana

## 1. Infrastructure & Data Model
- [ ] 1.1 Extend `AccountStatus` enum to include `Delinquent` (if it doesn't already exist).
- [ ] 1.2 Create `DelinquencyRecordEntity` with required properties (`AccountId`, `StatementId`, `OverdueAmount`, `DaysInArrears`, `Bucket`, `Status`).
- [ ] 1.3 Add `DbSet<DelinquencyRecordEntity>` to `CardVaultDbContext` and configure entity mappings (Table name, PK, constraints).
- [ ] 1.4 Generate EF Core migration (`AddDelinquencyRecords`) and apply to database.

## 2. Application Logic (CQRS)
- [ ] 2.1 Create `EvaluateDelinquencyCommand` (record accepting a `ReferenceDate` for testing/idempotency).
- [ ] 2.2 Create `EvaluateDelinquencyCommandHandler` in `CardVault.Application/Features/Delinquency/Commands/`.
- [ ] 2.3 Implement handler logic: query accounts with active/delinquent status and due date < reference date.
- [ ] 2.4 Implement bucket calculation (`1_TO_30`, `31_TO_60`, `61_TO_90`, `OVER_90`).
- [ ] 2.5 Implement status transition logic (Active -> Delinquent, Delinquent -> Active).
- [ ] 2.6 Persist changes using `CardVaultDbContext`.

## 3. Background Worker
- [ ] 3.1 Create `DelinquencyEvaluationWorker` implementing `BackgroundService` in `CardVault.Api/Background/`.
- [ ] 3.2 Implement daily timer loop (or equivalent periodic execution).
- [ ] 3.3 Dispatch `EvaluateDelinquencyCommand` from within a scoped DI context.
- [ ] 3.4 Register the hosted service in `Program.cs` (`builder.Services.AddHostedService<DelinquencyEvaluationWorker>()`).

## 4. Testing (Strict TDD)
- [ ] 4.1 Write test `EvaluateDelinquency_ShouldMarkAccountDelinquent_WhenMinimumPaymentNotMet`.
- [ ] 4.2 Write test `EvaluateDelinquency_ShouldCreateDelinquencyRecord_WithCorrectBucket`.
- [ ] 4.3 Write test `EvaluateDelinquency_ShouldNotAffectAccounts_WhenMinimumPaymentIsMet`.
- [ ] 4.4 Write test `EvaluateDelinquency_ShouldIncrementDaysAndBucket_WhenAging`.
- [ ] 4.5 Write test `EvaluateDelinquency_ShouldResolveDelinquency_WhenOverduePaid`.
