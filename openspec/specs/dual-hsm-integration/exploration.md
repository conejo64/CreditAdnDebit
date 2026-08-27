## Exploration: dual-hsm-integration

### Current State
The system currently uses a `MacService` which implements `IMacService`. This service simulates an HSM by computing a simple SHA256 hash and truncating it. There is no abstraction for different HSM backends or broader cryptographic operations (like PIN translation/validation), and it is hardcoded to use the simulation logic. 

### Affected Areas
- `backend/services/IsoSwitch/src/IsoSwitch.Infrastructure.SwitchIso8583/Iso/IMacService.cs` — Likely to be refactored or replaced by `IHsmService`.
- `backend/services/IsoSwitch/src/IsoSwitch.Infrastructure.SwitchIso8583/Iso/MacService.cs` — To be replaced by the `SoftHsmProvider` implementation.
- `backend/services/IsoSwitch/src/IsoSwitch.Api/Program.cs` — DI container registration must be updated to conditionally load the appropriate HSM provider.
- `backend/services/IsoSwitch/src/IsoSwitch.Api/appsettings.json` — Needs new configuration nodes (e.g., `Hsm:Provider`, `Hsm:TcpOptions`).

### Approaches
1. **Strategy Pattern via AppSettings (Ports & Adapters)** — Define `IHsmService` (the port). Implement `SoftHsmProvider` and `HardwareHsmProvider` (Thales payShield TCP) as adapters. Use `Program.cs` to read `appsettings.json` and inject the chosen adapter.
   - Pros: Adheres to clean architecture. Local development remains easy (SoftHSM). Fully testable.
   - Cons: Minor overhead in DI setup.
   - Effort: Medium

2. **Factory Pattern** — Inject an `IHsmFactory` that returns the appropriate HSM instance on-demand at runtime.
   - Pros: Can support multiple simultaneous HSMs.
   - Cons: Overkill for most environments which only use one active HSM type.
   - Effort: Medium

### Recommendation
**Strategy Pattern via AppSettings (Ports & Adapters)**. Create the `IHsmService` interface and two concrete implementations: `SoftHsmProvider` and `HardwareHsmProvider`. Toggle them in `Program.cs` based on the configuration. 

**Critical Requirement:** Implement this using **Strict TDD**. Write unit tests for the Thales command builders, parsers, and the SoftHSM crypto logic *before* writing the actual implementations. 

### Risks
- **Hardware Simulation in CI**: Real hardware HSM tests are impossible in CI without a dedicated simulator. TDD must heavily focus on message formatting and parsing instead of end-to-end integration.
- **Latency & Timeouts**: TCP connections to a hardware HSM can introduce latency. Circuit breaking and tight timeouts must be employed.

### Ready for Proposal
Yes — The orchestrator should tell the user that the exploration is complete and we are ready to move to the proposal phase focusing on the Strategy Pattern and Strict TDD.
