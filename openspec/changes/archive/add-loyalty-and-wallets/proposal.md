# Proposal: Add Loyalty And Wallets

## Why

Sprint 9 still has `v67` and `v69` pending for loyalty programs and digital wallet enablement. CardVault already owns cards, issuer accounts, tokenization, switch event consumption, and authorization controls, so it is the right bounded context to expose rewards and wallet provisioning without crossing PCI boundaries.

## Scope

Add CardVault-managed loyalty and wallet support with:

- configurable cashback and points earning programs by product
- balance tracking and entry history for rewards
- rewards catalog and redemption APIs
- wallet card enrollment with activation challenge
- wallet payment authorization using active wallet tokens and existing hold or risk flows
- integration-safe outbox and audit events for rewards and wallet actions

## Out Of Scope

- direct network certification with Apple Pay or Google Pay
- merchant-funded offers or MCC-specific loyalty rules beyond current demo dimensions
- frontend wallet UX

## Impacted Areas

- `backend/services/CardVault/src/CardVault.Api`
- `backend/services/CardVault/src/CardVault.Infrastructure.Persistence`
- `openspec/specs/`
- `funcional/Sprints.md`
