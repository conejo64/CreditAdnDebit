## Exploration: stress-testing-k6

### Current State
The `IsoSwitch` backend service processes transactions using MediatR and exposes various REST endpoints (e.g., `/api/iso/authorize`, `/api/iso/capture`, `/api/iso/reversal`). These endpoints translate incoming HTTP requests into corresponding MediatR commands (`AuthorizeTransactionCommand`, etc.), processing fields mapped to ISO8583 requirements and executing standard switch behaviors. 

Currently, there are no dedicated automated load/stress testing scripts in the repository.

### Affected Areas
- `backend/services/IsoSwitch/tests/` — New directory `k6/` needs to be created to hold the load testing scripts.
- `backend/services/IsoSwitch/tests/k6/isoswitch-load.js` — The primary K6 script file to simulate load.

### Approaches
1. **HTTP K6 Testing against REST Endpoints** — Create a K6 script that targets the exposed REST API (`/api/iso/authorize`) simulating transaction traffic.
   - Pros: Native K6 support for HTTP, easy to script payload variations (JSON), utilizes the exact API boundary designed for HTTP integrations, easily extensible.
   - Cons: Does not test raw TCP socket performance or binary ISO8583 packet parsing directly.
   - Effort: Low

2. **Custom K6 Extension for TCP/ISO8583** — Write a custom Go extension for K6 (xk6) to send raw ISO8583 binary/ASCII packets over a TCP socket.
   - Pros: Tests the full raw TCP networking pipeline if that's the primary integration point.
   - Cons: Significant overhead requiring custom Go code and custom K6 builds.
   - Effort: High

### Recommendation
**Approach 1 (HTTP K6 Testing against REST Endpoints)** is highly recommended. The .NET API already exposes robust HTTP endpoints (like `/api/iso/authorize`) specifically designed to trigger transaction flows inside the switch. An HTTP-based K6 script is native, simple to maintain, and effectively tests the backend MediatR handlers, database integration, and overall application scalability without requiring custom K6 extensions. Scripts should be stored in `backend/services/IsoSwitch/tests/k6/`.

### Risks
- Load testing against local infrastructure might overload Docker Compose containers or hit database connection pool limits.
- High transaction volumes might fill up the local Kafka topics and PostgreSQL instances quickly if cleanup scripts are not part of the testing cycle.
- Stress testing might generate a significant number of PCI-audit logs (though masked/tokenized) depending on simulated PANs.

### Ready for Proposal
Yes — the project structure and HTTP endpoints have been validated and are ready for a K6 implementation proposal.
