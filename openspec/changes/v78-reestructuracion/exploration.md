## Exploration: Sprint 10 - Reestructuración y Refinanciamiento
### Current State
Currently, CardVault handles deferred purchases via InstallmentPlanEntity and InstallmentService.DeferPurchaseAsync, which converts a specific LedgerEntry (Purchase) into an amortization schedule. However, there is no mechanism to consolidate existing debt (ledger balance + remaining installments + interest/fees) for delinquent accounts into a new refinanced agreement. Accounts that fall into delinquency (AccountStatus.Delinquent) have records in DelinquencyRecordEntity but lack a structured process to renegotiate their debt, reset their status, and generate a new payment schedule.

### Affected Areas
- **Database/Persistence (CardVault.Infrastructure.Persistence)**: Requires new entities to model restructuring agreements (RestructuringAgreementEntity) and its relation to InstallmentPlanEntity. Updates to AccountStatus.
- **Domain Logic (CardVault.Api/Services)**: A new RestructuringService to handle debt consolidation, compensating ledger entries, and new amortization generation.
- **API/Controllers (CardVault.Api/Controllers)**: Endpoints in DelinquencyController (or a new RestructuringController mapped to pi/collections/restructuring) to simulate, propose, and accept restructuring plans.
- **Frontend (rontend/src/app/collections)**: New UI to visualize eligible debt, calculate restructuring simulations (term, rate), and execute the refinancing.

### Approaches

#### Approach A: Reuse InstallmentPlanEntity directly
Instead of complex domain models, simply calculate the total owed balance, emit a compensating ledger entry to zero it out, and call InstallmentService to create a new InstallmentPlanEntity for the total amount with a custom description "Refinancing".
- **Pros**: Very fast to implement, reuses all existing billing and amortization code.
- **Cons**: Mixes normal purchase installments with collections-based refinancing. No clean way to track down payments (abono inicial), forgiven amounts (quitas), or track the legal status of the agreement.

#### Approach B: Dedicated RestructuringAgreementEntity + InstallmentPlan
Create a dedicated entity (RestructuringAgreementEntity) to track the legal/business terms of the renegotiation (original debt, forgiven interest, required down payment, status: Proposed, Accepted, Broken). When activated, it generates a specialized InstallmentPlanEntity for the mathematical amortization and issues the compensating ledger entries.
- **Pros**: Clean separation of collections domain from standard retail billing. Supports complex collections workflows (like down-payment verification before activation). Leaves a solid audit trail for risk analysis.
- **Cons**: Higher complexity, requires more tables and state management.

#### Approach C: Separate LoanAccountEntity
Migrate the debt entirely out of the Credit Card account and open a new Consumer Loan account.
- **Pros**: Cleanest financial separation.
- **Cons**: Massive scope creep. Requires a new loan processing engine outside the scope of CardVault.

### Recommendation
**Approach B**. It balances the need for specialized collections tracking (which is a core goal of Sprint 10) with the reuse of the existing InstallmentPlan amortization engine. It allows tracking the "Agreement" separately from the "Payment Plan".

### Risks
- **Concurrency**: Refinancing an account while the DailyInterestAccrualService or statement generation is running could lead to mismatched balances. The restructuring process must acquire a lock or check versioning.
- **Ledger Integrity**: The compensating entries must exactly match the consolidated debt to ensure the LedgerBalance zeros out correctly before the new installment plan takes over.
- **Broken Agreements**: If a customer defaults on a restructured plan, we need a way to track that this is a "broken promise" (which usually carries harsher risk penalties).

### Ready for Proposal
Yes. The problem is well understood, the domain boundaries within CardVault are clear, and the recommended approach aligns with the SDD architecture. We can proceed to create the formal proposal.md.
