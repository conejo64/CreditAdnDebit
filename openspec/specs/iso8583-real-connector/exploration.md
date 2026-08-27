## Exploration: iso8583-real-connector

### Current State
`IsoSwitch` currently routes transactions through the `IAcquirerConnector` interface. By default, it uses a `SimulatorConnector` configured with the "SIMULATOR" ID, which communicates synchronously over TCP via `TcpIsoClient` to `IsoSimulatorServer` (a local background service listening on port 5005). The simulation is basic, echoing request fields into a static response code (e.g., 0100 -> 0110). There is a generic `TcpGatewayConnector` available, but the system lacks the specific packagers, network management flows, and MAC calculation logic needed for real Ecuador networks (Banred, Datafast).

### Affected Areas
- `backend/services/IsoSwitch/src/IsoSwitch.Infrastructure.SwitchIso8583/Connectors/` — New connectors for Banred and Datafast need to be added.
- `backend/services/IsoSwitch/src/IsoSwitch.Infrastructure.SwitchIso8583/Iso/` — Network-specific packagers (implementing `IIso8583Packager`) must be created to handle distinct ISO dialects, bitmaps, and field structures.
- `backend/services/IsoSwitch/src/IsoSwitch.Infrastructure.SwitchIso8583/Iso/MacService.cs` — The current placeholder SHA256 MAC implementation needs to support real MAC generation (like ISO9797/ANSI X9.19 or HSM-based integration).
- `backend/services/IsoSwitch/src/IsoSwitch.Application/Config/ConnectorRegistry.cs` — Needs to register the new Banred/Datafast connectors instead of defaulting to SIMULATOR.

### Approaches
1. **Reuse `TcpGatewayConnector` with Specific Packagers**
   - Configure multiple instances of the existing `TcpGatewayConnector` pointing to Banred and Datafast TCP endpoints. 
   - Inject network-specific packagers (e.g. `BanredIso8583Packager`) into the `PackagerRegistry`.
   - Pros: Minimal boilerplate; reuses the existing, well-tested `TcpIsoClient` loop and configuration bindings.
   - Cons: Complex DI registration for multiple instances; awkward to handle network-specific lifecycle tasks (Sign-on, Echo, dynamic key exchange) within a generic gateway connector.
   - Effort: Medium

2. **Dedicated Connectors (`BanredConnector`, `DatafastConnector`)**
   - Create explicitly typed classes implementing `IAcquirerConnector` that internally wrap a tailored `TcpIsoClient`.
   - Implement bespoke `BanredIso8583Packager` and `DatafastIso8583Packager`.
   - Pros: Clean separation of concerns; simplifies adding network-specific handshakes, connection lifecycle management, and specific MAC or TPDU headers.
   - Cons: Slightly more initial boilerplate.
   - Effort: Medium

3. **Kafka Outbox / Integration**
   - Offload the external TCP communication entirely to asynchronous Kafka workers.
   - Pros: Extreme resilience against socket drops; highly scalable.
   - Cons: Fundamentally changes the current synchronous `Task<IsoMessage>` design of `IAcquirerConnector`; introduces latency not ideal for ISO8583 timeouts.
   - Effort: High

### Recommendation
Approach 2 (**Dedicated Connectors**) is the recommended path. Real acquirer networks like Banred and Datafast typically require unique network management messages (0800 sign-on, key exchange) and specific TCP framing/TPDU headers. Encapsulating this inside dedicated connector classes (e.g., `BanredConnector` wrapping `TcpIsoClient`) maintains a clean architecture. Specific packagers must also be implemented to replace the generic `Iso8583Codec` since Banred/Datafast will likely use distinct dialects of ISO8583 (e.g. 1987 vs 1993) and potentially EBCDIC or custom MAC locations.

### Risks
- **HSM/MAC Integration**: `MacService.cs` currently uses a placeholder SHA256 MAC. The real networks will reject messages without proper HSM-backed MACing. 
- **Timeouts & Circuit Breakers**: The existing `TcpIsoClient` circuit breaker might be too simple if the network has specific rules on connection recycling during partial outages.
- **Spec Drift**: If we lack exact Banred/Datafast ISO8583 specification documents, the custom packagers will be incorrect.

### Ready for Proposal
Yes — Tell the user that the exploration is complete and saved to `openspec/specs/iso8583-real-connector/exploration.md` and Engram. Suggest moving on to the Proposal phase using Approach 2 (Dedicated Connectors).
