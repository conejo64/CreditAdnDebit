# Design: SEC-9 / CICD-13 Config Secret Remediation

## Architectural Position

Nine decisions layered onto an existing .NET 9 / ASP.NET Core solution with three service-autonomous bounded contexts (CardVault, IsoSwitch, IsoAudit) plus one shared messaging library (`backend/shared/BuildingBlocks`). No new bounded context, capability, service boundary, schema, or runtime contract change.

Governing principle: **the mechanism's scope SHALL be derived at run time, never transcribed by hand.** Every decision is judged against one question: *can this control silently become narrower than the requirement it enforces?* Where the answer is yes, the design removes the hand-transcribed list or pins it as a **floor** that can only under-assert, never falsely pass.

Conventions followed rather than invented (all verified against the current tree):

- Per-service duplication of `Security/*Options.cs` + `*OptionsValidator.cs`. `JwtOptions`/`JwtOptionsValidator` exist as three independent copies. There is **no** shared cross-service host library: `BuildingBlocks.csproj` references only `Confluent.Kafka`, `Microsoft.Extensions.Hosting.Abstractions`, `Microsoft.Extensions.Logging.Abstractions` — no `Microsoft.Extensions.Options`.
- Secrets needing startup validation are properties on a validated options type (`JwtOptions.SigningKey`, `TokenizationOptions.Secret`, `AdminApiKeyOptions.ApiKey`, `RequiredConnectionStringsOptions.Postgres`). Secrets consumed at a provider boundary omit the property (`SendGridOptions`, `MovistarOptions`). Two different rules, not a contradiction.
- Validators return `ValidateOptionsResult.Fail` naming the exact configuration key and reject known DEV placeholders by substring (`AdminApiKeyOptionsValidator:14-15` forbids `dev-admin-key`).

### Sequencing

```
PR1 (discovery test + spec scope + appsettings purge)
  +-- PR2 IsoSwitch fail-fast  \
  +-- PR3 IsoAudit fail-fast    | order-independent among themselves
  +-- PR4 design-time factories |
  +-- PR5 Kafka SigningSecret  /
        -> PR6 (ATOMIC) -> PR7 (blocking full-tree gate)
```

**PR6's atomicity is more load-bearing than the proposal argued.** The proposal's reason (widening the ruleset while compose still holds `POSTGRES_PASSWORD: postgres` would fire on PR6's own diff) is correct but incomplete. There is a *mutual* dependency: the remediated line `Password=${POSTGRES_PASSWORD:?...}` still matches the **existing** `inline-connection-string-password` rule (`${POSTGRES_PASSWORD:?set` is 24 non-excluded characters after `Password=`), so the `${...}` interpolation allowlist must land **with or before** the compose edit, while the new detection rules must land **after** the purge. Only a single commit satisfies both constraints. Splitting PR6 is not merely undesirable — it is impossible without a red intermediate state.

---

## ADR-1 — Repo-root discovery for `CommittedConfigSecretShapeTests`

**Context.** `CommittedConfigSecretShapeTests.cs:76-78` is a `[Theory]` with two hardcoded `[InlineData]` paths. It must become run-time discovery. The test assembly's working directory is `bin/Debug/net9.0/...`.

**Starting evidence (read first, as instructed).** The file **already has** `FindRepoRoot()` at lines 22-33: it walks up from `AppContext.BaseDirectory` via `DirectoryInfo.Parent` until it finds a directory containing a `backend` subdirectory, and throws `InvalidOperationException` if it reaches the filesystem root. `LoadJson()` (lines 35-42) then combines that root with a repo-relative path.

**Decision: reuse and harden the existing walk-up. Do not introduce a build-time mechanism.**

Harden the sentinel from a single `backend` directory to a multi-marker predicate — the root must contain **both** a `backend` and an `openspec` directory:

```csharp
private static bool IsRepoRoot(DirectoryInfo d) =>
    Directory.Exists(Path.Combine(d.FullName, "backend")) &&
    Directory.Exists(Path.Combine(d.FullName, "openspec"));
```

**Why this over the alternatives.** The walk-up is already **empirically proven** in both working directories that matter: this exact helper backs five currently-green `[Fact]`s, and the suite runs green in CI (`.github/workflows/ci.yml:50`, `dotnet test -c Release --no-build`). That is stronger evidence than any argument about a mechanism not yet in the tree.

- **Rejected — MSBuild-injected property** (`<AssemblyMetadata Include="RepoRoot" Value="$(MSBuildThisFileDirectory)../../../.." />`). More deterministic in principle and immune to a pathological ancestor directory, but it adds a build-time coupling to a `.csproj`, silently bakes a machine-specific absolute path into the assembly, and breaks if the test output is ever copied. Cost exceeds benefit when the walk-up already works.
- **Rejected — assembly attribute.** Same coupling as MSBuild injection with an extra indirection.
- **Rejected — `.git` as sentinel.** Fails in a worktree or submodule, where `.git` is a *file*, not a directory. `backend` and `openspec` are ordinary tracked directories present in every checkout shape.
- **Rejected — `*.sln` search.** `CardSwitchPlatform.sln` lives at `backend/CardSwitchPlatform.sln`, **not** at the repo root, so it identifies the wrong directory.

**Discovery implementation.**

```csharp
private static readonly EnumerationOptions Options = new()
{
    RecurseSubdirectories = true,
    IgnoreInaccessible    = true,
    MatchCasing           = MatchCasing.CaseInsensitive,
};

private static IReadOnlyList<string> DiscoverAppSettingsFiles()
{
    var root = FindRepoRoot();
    return Directory.EnumerateFiles(root, "appsettings*.json", Options)
        .Where(p => !IsExcluded(root, p))
        .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
        .ToList();
}
```

`IsExcluded` matches on **path segments**, case-insensitively, split on both directory separators — never a substring test, which would wrongly drop a hypothetical `.../object-store/...`. Excluded segments: `bin`, `obj`, `node_modules`, `.git`.

- `EnumerationOptions.IgnoreInaccessible = true` is load-bearing: the plain `Directory.GetFiles(..., SearchOption.AllDirectories)` overload throws `UnauthorizedAccessException` on the first unreadable directory, which turns a security test into a flaky environment test on a developer machine.
- `bin`/`obj` exclusion is mandatory, not hygiene: the API projects copy `appsettings.json` into `bin/Debug/net9.0/`, so without it every finding is duplicated, and a stale build output could be clean while the source file is dirty (or the reverse) — non-determinism in the exact control that must be trustworthy.

**Zero-discovery behaviour — the anti-vacuous-pass guard.** This is the crux: naive discovery inherits the old bug in a new form. `Directory.EnumerateFiles` returning an empty sequence makes an `Assert.All`-style test pass with zero assertions executed. A broken exclusion filter, a moved sentinel, or a renamed directory would then report GREEN with nothing verified — exactly the failure mode this change exists to eliminate, re-created one layer down.

**Decision: floor-and-superset assertion.**

1. **Floor** — a dedicated `[Fact]` asserts the discovered set is non-empty **and** is a **superset** of a hardcoded minimum manifest: the six `appsettings*.json` files SEC-9 names (CardVault, IsoSwitch, IsoAudit × base + Development). Discovery failure, a bad filter, or a silently relocated file all fail loudly here.
2. **Exhaustive assertion** — the shape checks (`ConnectionStrings` values contain no `Password=`) run over the *full discovered set*, so a seventh file or a fourth service is covered automatically with no human action.

The hardcoded manifest's role is **inverted** relative to the `[InlineData]` list it replaces, and this inversion is the whole point:

| | Old `[InlineData]` list | New minimum manifest |
|---|---|---|
| Role | **ceiling** — defines the entire scope | **floor** — defines a lower bound |
| A stale entry causes | a **false PASS** on an unlisted leaking file | a **false FAIL**, never a false pass |
| A new service is | invisible until a human adds a line | covered automatically |

A stale floor can only under-assert by omission; it can never let a discovered file escape the shape check. That asymmetry is what makes the mechanism trustworthy where the old one was not.

**Verified count.** Six `appsettings*.json` under `backend/services/*/src/*.Api/`. Leaks at IsoSwitch base `:43`, IsoAudit base `:3`, IsoAudit Development `:3`.

---

## ADR-2 — Fail-fast connection-string validators for IsoSwitch and IsoAudit

**Context.** CardVault has `RequiredConnectionStringsOptions` + `RequiredConnectionStringsOptionsValidator` in `CardVault.Api/Security/`, wired at `Program.cs:154-158` via `AddOptions<T>().BindConfiguration("ConnectionStrings").ValidateOnStart()` + `AddSingleton<IValidateOptions<T>, TValidator>()`. IsoSwitch registers `AddDbContext<IsoSwitchDbContext>` at `Program.cs:103-107` with no validator. IsoAudit carries the in-code fallback at `Program.cs:62-63`.

**Decision: duplicate the pair per service. Do not create shared code.**

| Service | New files | Options property | Validated key |
|---|---|---|---|
| IsoSwitch | `IsoSwitch.Api/Security/RequiredConnectionStringsOptions{,Validator}.cs` | `Postgres` | `ConnectionStrings:Postgres` |
| IsoAudit | `IsoAudit.Api/Security/RequiredConnectionStringsOptions{,Validator}.cs` | `IsoSwitchDb` | `ConnectionStrings:IsoSwitchDb` |

**Why duplication over a shared library — checked before proposing, as instructed.**

- **Rejected — put it in `BuildingBlocks`.** Verified: `BuildingBlocks.csproj` has **no** `Microsoft.Extensions.Options` reference. It is a Kafka/outbox messaging library; adding options-validation infrastructure would require a new package reference and would push ASP.NET Core host concerns into a transport library that all three services consume. Wrong layer.
- **Rejected — a new `backend/shared/Hosting` project.** Creating a cross-service library to share **21 lines of code with a different property name per service** inverts the cost/benefit, adds a `.csproj` to the solution and a build edge to all three services, and contradicts the repo's demonstrated choice: `JwtOptions`/`JwtOptionsValidator` are already duplicated three times rather than shared. Service autonomy is the established pattern, and a security remediation is the wrong vehicle to reverse it.
- **Consequence accepted:** three near-identical validators. The duplication is intentional and bounded — each validates a *different* key set, so the shared surface is the shape, not the logic.

**Correction to an input assumption.** The proposal states IsoAudit has "no validator infrastructure at all". **Verified false:** `IsoAudit.Api/Security/` contains `JwtOptions.cs` and `JwtOptionsValidator.cs`, and `Program.cs:22-26` already wires `AddOptions<JwtOptions>().BindConfiguration(...).ValidateOnStart()` + `AddSingleton<IValidateOptions<JwtOptions>, JwtOptionsValidator>()`. IsoAudit has exactly the pattern to copy, three lines above where the new registration goes. PR3 is therefore **less** net-new code than its 150-200 line estimate, not more.

**IsoAudit fallback removal.** `Program.cs:62-63` becomes `opt.UseNpgsql(builder.Configuration.GetConnectionString("IsoSwitchDb"))`, matching IsoSwitch `Program.cs:105` and CardVault `Program.cs:169` exactly. No `??`, and no `?? string.Empty` either — a coalesce is a fallback wearing a different hat.

**Sequencing gotcha — `ValidateOnStart` fires AFTER the pre-`Run()` database bootstrap.** `ValidateOnStart` runs during host start (`app.Run()` → `StartAsync`). IsoAudit resolves `IsoSwitchDbContext` and calls `EnsureCreatedAsync`/`MigrateAsync` at `Program.cs:73-82`, i.e. **before** `app.Run()` at line 104. So with a genuinely absent connection string, an operator may see a raw Npgsql exception from the bootstrap block *before* the validator's key-naming message.

This is **pre-existing and uniform**, not introduced here: IsoSwitch has the identical structure (`Program.cs:176-185`), as does CardVault. All three already behave this way today. The validator still delivers SEC-9's message contract, and the common post-purge case is an empty string rather than null (the purged `appsettings.json` retains `"IsoSwitchDb": ""`), which `UseNpgsql("")` tolerates — empirically proven by IsoSwitch running green on `"Postgres": ""` today.

Fixing the ordering properly means moving the bootstrap into a hosted service. IsoAudit already has an **unused** `DbMigrateWorker : BackgroundService` at `Program.cs:106-125` that is defined and never registered — the obvious vehicle. **Deliberately out of scope:** it changes startup behaviour for all three services and belongs in its own change. Flagged as a documented follow-up, not silently absorbed.

---

## ADR-3 — Kafka `SigningSecret` fail-fast

**Context — read how it is bound before deciding.** There is **no** `KafkaOptions` type anywhere in the solution. Both consumers use raw `IConfiguration` indexing:

| Site | Code |
|---|---|
| `CardVault.Api/Program.cs:313-314` | `var kafka = builder.Configuration.GetSection("Kafka");` → `new KafkaEventBus(..., kafka["SigningSecret"])` |
| `IsoSwitch.Api/Program.cs:133` | `signingSecret: cfg["Kafka:SigningSecret"]` (retry republisher) |
| `IsoSwitch.Infrastructure.Consumers/PciAuditConsumer.cs:22` | `signingSecret: cfg["Kafka:SigningSecret"]` |
| IsoAudit | **none** — `IsoAuditConsumerWorker` (`Program.cs:127-210`) builds a raw `ConsumerConfig` and never signs or verifies |

**The finding that makes this non-optional.** Signing is **opt-in and silently degrading on both ends**:

- `KafkaEventBus.cs:27` — `_signingSecret = string.IsNullOrWhiteSpace(signingSecret) ? null : signingSecret;` then `:39-40` — `if (_signingSecret is not null) KafkaMessageSecurity.Sign(...)`
- `KafkaConsumerWorker.cs:38` — identical normalisation; `:79` — `if (_signingSecret is not null && !KafkaMessageSecurity.Verify(...))`

So **emptying `Kafka:SigningSecret` in committed config without adding startup validation silently turns off Kafka message signing across the platform** — the producer stops signing, the consumer stops verifying, and nothing logs a warning. The purge alone would convert a committed-placeholder problem into a disabled-integrity-control problem. Fail-fast validation is not a nicety attached to the purge; it is what keeps the purge from being a net security regression.

**Decision: a new dedicated `KafkaSigningOptions` + `KafkaSigningOptionsValidator` per service, in CardVault and IsoSwitch only.**

```
{CardVault,IsoSwitch}.Api/Security/KafkaSigningOptions.cs           { public string? SigningSecret { get; set; } }
{CardVault,IsoSwitch}.Api/Security/KafkaSigningOptionsValidator.cs
```

Bound with `AddOptions<KafkaSigningOptions>().BindConfiguration("Kafka").ValidateOnStart()`. Binding the whole `Kafka` section while declaring only the one property is exactly what `RequiredConnectionStringsOptions` does with `ConnectionStrings`; extra keys in the section bind to nothing and are ignored.

The validator mirrors `AdminApiKeyOptionsValidator` (`IsoSwitch.Api/Security/AdminApiKeyOptionsValidator.cs`), with its forbidden-substring list extended by the literal being purged:

```csharp
private static readonly string[] Forbidden =
    ["DEV_ONLY", "CHANGE_ME", "change_me", "placeholder", "dev-signing-secret"];
```

The failure message names the key: `"Kafka:SigningSecret ..."`. **No minimum length** — unlike `Admin:ApiKey` (32 characters) and `Jwt:SigningKey`, this is an HMAC-SHA256 key with no length precedent in the codebase; inventing one would be an undiscussed policy change. Non-empty and not-a-known-placeholder is the requirement.

**Rejected alternatives.**

- **Extend `RequiredConnectionStringsOptions`.** That type is documented (`RequiredConnectionStringsOptions.cs:3-10`) as binding `ConnectionStrings` purely to hang `ValidateOnStart()` off. A signing secret is not a connection string, lives in a different configuration section, and cannot bind there. Merging unrelated secrets into one bag also degrades the failure message from "this key" to "one of these keys".
- **Introduce a full `KafkaOptions`** covering `BootstrapServers`/`ClientId`/`Topics`/`Retry`/`Dlq`/`RetryRepublisher`. Correct long-term, but it touches every `kafka["..."]` call site across two services and three files — precisely the blast radius the proposal excluded when it deferred runtime connection-string-key unification. Same reasoning, same answer: out of a security remediation's scope. Documented follow-up.
- **Add it to IsoAudit too.** IsoAudit neither signs nor verifies. A validator demanding a secret the service never uses would fail startup on a value with no consumer — an invented requirement.

**Apparent convention conflict, resolved.** The repo documents that "`ApiKey` is a secret and is intentionally NOT a property here" (`SendGridOptions`/`MovistarOptions`). That rule applies to secrets consumed at a **provider boundary** with no startup validation. Secrets that must be *validated at startup* are properties on a validated options type — `JwtOptions.SigningKey`, `TokenizationOptions.Secret`, `AdminApiKeyOptions.ApiKey`, and `RequiredConnectionStringsOptions.Postgres`, which literally holds a password-bearing string. Consistent, not contradictory.

**Test-factory consequence — will break the suite if missed.** `CardVaultWebApplicationFactory.cs:43-61` supplies `Jwt:SigningKey`, `Vault:Keys:*` and both connection strings via `UseSetting` precisely so the existing validators pass. Options validation runs on host start regardless of `ConfigureTestServices` removing every `IHostedService` (lines 71-74) and stubbing `IEventBus` (lines 76-80). So all three web-application factories that boot a service carrying the new validator **must** add `builder.UseSetting("Kafka:SigningSecret", "TestKafkaSigningSecretForIntegrationTests")` — a value containing no `Forbidden` substring.

---

## ADR-4 — Design-time factory shape

Mechanism and naming are settled inputs. Recorded here for consistency across the three files.

| Factory | File:line | Old env var | New env var |
|---|---|---|---|
| `IsoSwitchDbContextFactory` | `IsoSwitch.Infrastructure.Persistence/IsoSwitchDbContextFactory.cs:12-13` | `ISOSWITCH_POSTGRES` | `ConnectionStrings__Postgres` |
| `IdentityAppDbContextFactory` | `CardVault.Infrastructure.Identity/Auth/IdentityAppDbContextFactory.cs:12-13` | `CARDVAULT_SQLSERVER` | `ConnectionStrings__SqlServerIdentity` |
| `CardVaultDbContextFactory` | `CardVault.Infrastructure.Persistence/CardVaultDbContextFactory.cs:13-14` | `CARDVAULT_POSTGRES` | `ConnectionStrings__CardVaultPostgres` |

All three collapse to one shape — a single guard clause, no `??`:

```csharp
var cs = Environment.GetEnvironmentVariable("ConnectionStrings__Postgres");
if (string.IsNullOrWhiteSpace(cs))
    throw new InvalidOperationException(
        "ConnectionStrings__Postgres is not set (replaces ISOSWITCH_POSTGRES). " +
        "Set it before running dotnet ef — see backend/deploy/.env.example.");
```

**Why `InvalidOperationException` and not `OptionsValidationException`.** These are `IDesignTimeDbContextFactory` implementations invoked by the `dotnet ef` CLI. There is no host, no DI container and no options pipeline — `IValidateOptions` is unavailable by construction. `InvalidOperationException` with an actionable message is the only shape available, and `dotnet ef` surfaces its message to the developer verbatim.

**Naming asymmetry, deliberate.** `ConnectionStrings__CardVaultPostgres` is the design-time key only; CardVault's **runtime** key stays `ConnectionStrings:Postgres`. Reusing `ConnectionStrings__Postgres` for CardVault's design-time factory would make `dotnet ef` target **IsoSwitch's** database, because that key is IsoSwitch's in the shared `.env` — which is exactly why `docker-compose.yml:53-56` carries the inline "W-1 fix" override. An awkward name beats a silent wrong-database migration.

**Design-time and runtime values are NOT interchangeable** — a trap worth stating because the naming invites the assumption. Design-time runs on the developer's host and reaches Postgres through the published port (`Host=localhost;Port=15432`, per `docker-compose.yml:11`); runtime runs inside the compose network (`Host=postgres;Port=5432`). Same logical database, different addresses. `.env.example` must carry them as distinct entries **with a comment saying so**, or the first developer to "deduplicate" them will break `dotnet ef`.

---

## ADR-5 — The gitleaks ruleset: two new rules and one precision fix

**Context.** `.gitleaks.toml` has `[extend] useDefault = true` plus two custom rules. `inline-connection-string-password` (`:17`) requires `=`, so YAML `KEY: value` credentials are invisible.

**Hard constraint: gitleaks runs Go's `regexp` (RE2).** No lookahead, no lookbehind, no backreferences. Every "match X but not Y" must be expressed as a character class, a rule-level `path` filter, or an allowlist entry. This rules out the obvious `(?!__REPLACE_ME__)` formulation.

### Rule A — precision fix to the existing rule (real false positives found)

```toml
[[rules]]
id = "inline-connection-string-password"
description = "Inline password inside an ADO.NET/EF connection string"
regex = '''(?i)[;"'\s](Password|Pwd)\s*=\s*[^;"'\s]{4,}'''
tags = ["connection-string", "password"]
  [rules.allowlist]
  regexes = ['''\$\{[A-Za-z_][A-Za-z0-9_]*''']
  paths   = ['''\.md$''']
```

The current regex matches `frontend/src/app/features/auth/login.component.ts:435` (`showPassword = false;`) and `:447` (`this.showPassword = !this.showPassword;`). Both are genuine false positives, verified by reading the file. **The v2 exploration's claim that this file is a false positive only because it "contains the string `password` as an HTML form-control type/label" is wrong**: the matches are TypeScript identifiers `showPassword = ...`, and they do match the rule as written.

Requiring a `[;"'\s]` delimiter before the keyword kills both, because `Password` there is a camelCase identifier *suffix* (preceded by `w`). It cannot hide a real finding: ADO.NET connection strings always delimit keywords with `;` or begin the string literal (preceded by a quote), and a line-initial occurrence is preceded by `\n`, which `\s` covers. Only a truly file-initial `Password=` is missed.

**This is a narrowing inside a change whose thesis is "widen the controls" — justified explicitly.** It removes *only* matches where the keyword is an identifier suffix, a class that by construction cannot be a credential literal. The alternative — allowlisting two lines of a live frontend source file — is strictly worse: it creates a permanent blind spot on a real file, whereas the tightening is a precision gain. Prefer fixing a rule over blinding it.

### Rule B — YAML-shaped container-bootstrap credentials (new)

```toml
[[rules]]
id = "yaml-credential-key"
description = "Container-bootstrap credential assigned with YAML colon syntax"
regex = '''(?i)[A-Za-z0-9]+_(PASSWORD|SECRET)\s*:\s*['"]?[^\s'"#]{4,}'''
path  = '''\.ya?ml$'''
tags  = ["container-bootstrap", "password", "yaml"]
  [rules.allowlist]
  regexes = ['''\$\{[A-Za-z_][A-Za-z0-9_]*''']
```

The `path` filter is the single most important element. Without it the rule fires on `CardVault.Tests/Features/Notifications/Templates/PciTemplateGuardTests.cs:90` (`AdditionalData = "otp_secret:JBSWY3DPEHPK3PXP"`, verified present) and on every markdown table in this change's own `proposal.md`/`exploration.md` that quotes `POSTGRES_PASSWORD:`. Restricting a YAML-shape rule to YAML files is not a weakening — it is the rule's actual domain.

### Rule C — message-signing secrets (new)

```toml
[[rules]]
id = "message-signing-secret"
description = "Kafka/HMAC message-signing secret in committed configuration"
regex = '''(?i)"signing[_-]?secret"\s*:\s*"[^"]{4,}"'''
tags  = ["kafka", "signing", "message-integrity"]
```

Requiring the **quoted-key JSON shape** is what makes this safe. The bare form `(?i)SigningSecret\s*[:=]` would fire on `PciAuditConsumer.cs:22` (`signingSecret: cfg["Kafka:SigningSecret"],` → `cfg[` is 4 characters) and on `IsoSwitch.Api/Program.cs:133`. Demanding `"` immediately before `signing` excludes both the C# named argument (`signingSecret:`, unquoted) and the configuration-key string (`"Kafka:SigningSecret"`, where `:` precedes the keyword).

### Match matrix — verified against actual repo strings

**Must match** (all verified present):

| String | File:line | Rule |
|---|---|---|
| `POSTGRES_PASSWORD: postgres` | `docker-compose.yml:5` | B |
| `MSSQL_SA_PASSWORD: "Your_strong_Passw0rd!"` | `docker-compose.yml:17` | B (`SA_` + `PASSWORD`) |
| `...Username=postgres;Password=postgres` | `docker-compose.yml:56` | A (`;` delimiter) |
| `"SigningSecret": "dev-signing-secret"` | `IsoSwitch.Api/appsettings.Development.json:26` | C |
| `"SigningSecret": "dev-signing-secret"` | `CardVault.Api/appsettings.Development.json:9` | C |
| `"Postgres": "Host=postgres;...Password=postgres"` | `IsoSwitch.Api/appsettings.json:43` | A |
| `"IsoSwitchDb": "...Password=postgres"` | `IsoAudit.Api/appsettings.json:3`, `appsettings.Development.json:3` | A |
| `?? "...Password=postgres"` | `IsoAudit.Api/Program.cs:63` | A |
| `?? "...Password=postgres"` | `IsoSwitchDbContextFactory.cs:13`, `CardVaultDbContextFactory.cs:14` | A |
| `?? "...Password=Your_strong_Passw0rd!..."` | `IdentityAppDbContextFactory.cs:13` | A |

**Must NOT match:**

| String | Where | Why not |
|---|---|---|
| `POSTGRES_USER: postgres`, `POSTGRES_DB: postgres` | compose:6-7 | no `_PASSWORD`/`_SECRET` |
| `ACCEPT_EULA: "Y"` | compose:16 | no keyword; value < 4 chars |
| `KAFKA_ENABLE_KRAFT=yes`, `KAFKA_CFG_*=...` | compose:26-34 | `=` not `:`; no keyword |
| healthcheck `test:`/`interval:`/`retries:` | none today | no keyword — future-proof |
| `showPassword = false;` / `!this.showPassword` | `login.component.ts:435,447` | Rule A delimiter class |
| `signingSecret: cfg["Kafka:SigningSecret"]` | `PciAuditConsumer.cs:22` | Rule C needs `"` before `signing` |
| `(secret: Kafka:SigningSecret)` | `backend/README.md:193` | Rule C shape |
| `AdditionalData = "otp_secret:JBSWY..."` | `PciTemplateGuardTests.cs:90` | Rule B `path` = `.ya?ml$` |
| `POSTGRES_PASSWORD: ${POSTGRES_PASSWORD:?...}` | compose, post-fix | Rule B `${` allowlist |
| `Password=${POSTGRES_PASSWORD:?...}` | compose, post-fix | Rule A `${` allowlist |

**The `${...}` allowlist is mandatory, not defensive.** Without it PR6's own remediated compose lines are findings, and PR6 cannot merge past the existing PR-diff scan. A shell/compose interpolation reference is by definition a pointer, never a literal — allowlisting it adds no blind spot.

**`__REPLACE_ME__` coverage under the new rules — confirmed, four ways.** `backend/deploy/.env.example` remains covered by (1) the global path allowlist `.gitleaks.toml:35`, (2) the global regex allowlist `__REPLACE_ME__` (`:41-43`), (3) Rule B's `path` filter, which excludes non-YAML files, and (4) Rule C's quoted-JSON shape, which cannot match `KEY=value` dotenv syntax. No new coverage is required. The `docs/env\.example` path entry at `:36` is dropped with the file. Also update the stale comment at `.gitleaks.toml:29-33`, which describes already-purged CardVault/IsoSwitch leaks and omits every file currently leaking.

---

## ADR-6 — Full-tree CLI invocation (PROVISIONAL — requires apply-time confirmation)

**I could not verify the gitleaks v8.30.1 CLI interface first-hand.** This execution context has no shell, no network fetch, and no gitleaks binary. Per instructions I am recording the recommended shape and marking it for confirmation rather than asserting it. The gitleaks CLI renamed subcommands across v8 minors, so memory is not adequate evidence.

**One decision I can make on structural grounds, independent of flag syntax: the scan must be working-tree mode, not git-history mode.**

`gitleaks` git mode scans **commits**. The proposal places git-history rewrite explicitly out of scope, and the archived Phase 0 design (`archive/2026-07-24-phase0-security-blockers/design.md:43-45`) defines the SEC-01 `git filter-repo` scrub as an out-of-band human operation outside the PR diff — I cannot confirm from this context whether it was ever executed. If it was not, the historically-leaked `Vault:Keys` k1/k2 values are still in history and a blocking full-history scan would be **permanently red with no in-scope remedy**. Working-tree mode is both what "full-tree" means in the requirement and the only option robust to that uncertainty.

**Recommended shape (confirm before use):**

```yaml
  secret-scan:
    name: Secret Scan (gitleaks, full tree)
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4        # no fetch-depth: 0 — working-tree scan needs no history
      - name: Run gitleaks (full tree)
        run: |
          docker run --rm -v "${{ github.workspace }}:/repo" \
            ghcr.io/gitleaks/gitleaks:v8.30.1 \
            dir /repo --config /repo/.gitleaks.toml --redact --verbose --no-banner
```

Blocking behaviour comes free: gitleaks exits non-zero on findings, which fails the step, which fails the required check. No `continue-on-error`, no `|| true`. Note that `fetch-depth: 0` is deliberately **dropped** — it exists at `ci.yml:20` only so the action can compute a diff range, and keeping it on a working-tree scan is misleading cargo cult.

**Every item below must be confirmed at apply time via `gitleaks version` and `gitleaks dir --help` against the pinned image, before PR7's body claims a green run:**

| # | Uncertainty | Fallback |
|---|---|---|
| 1 | `gitleaks dir <path>` is the v8.30.1 filesystem subcommand | legacy `gitleaks detect --source <path> --no-git` |
| 2 | config flag is `--config`/`-c` | `--config-path` |
| 3 | `.gitleaksignore` is auto-discovered at the scan root vs needs `--gitleaks-ignore-path` | pass it explicitly |
| 4 | tag `ghcr.io/gitleaks/gitleaks:v8.30.1` exists | `zricethezav/gitleaks:v8.30.1` — Phase 0 verified against `zricethezav/gitleaks:latest` (`archive/2026-07-24-phase0-security-blockers/tasks.md:32`), so that registry is known-good here |
| 5 | exit code is 1 on findings, 0 on clean | `--exit-code N` |
| 6 | **fingerprint format differs between git and no-git mode** (`commit:file:rule:line` vs `file:rule:line`) | see ADR-8 — this one is load-bearing |
| 7 | per-rule `[rules.allowlist]` with `paths`/`regexes`, and rule-level `path`, are supported in v8.30.1 | fold into the global `[allowlist]` and accept broader scope |

**Rejected — `gitleaks/gitleaks-action@v2`.** Requires `ACTIONS_ALLOW_USE_UNSECURE_NODE_VERSION` after 2026-06-02, stops working entirely after 2026-09-16, and is diff-scoped on `pull_request` — the defect being fixed.

**Rejected — `pre-commit run --all-files` in CI.** Would guarantee local/CI parity via one pinned `rev`, but adds a Python toolchain to the job and runs `gitleaks protect --staged` semantics (staged changes), not a full tree. Wrong scope.

**Rejected — pinned binary download.** Viable (`curl` the release tarball plus checksum) and preferable if the image tag turns out not to exist, but it adds checksum maintenance that a Docker digest handles for free. Keep as the fallback for uncertainty #4.

**Version parity.** `v8.30.1` matches `.pre-commit-config.yaml:3` exactly, so a developer's hook and CI cannot return different verdicts on the same tree. Any future bump must move both files in one commit — worth stating in the spec.

**Local-run gotcha to document.** In working-tree mode gitleaks scans files on disk. A developer with a populated `backend/deploy/.env` will see their **real** secrets flagged when running the documented command locally. That is correct behaviour (the file is gitignored and can never be committed), but it means the local run is not always green and the CI verdict on a clean checkout is the authoritative one. Do **not** allowlist `.env` to make the local run quiet — that is the blinding pattern this change exists to eliminate.

---

## ADR-7 — Compose interpolation vs `env_file`, and the duplicate key

**These are two different mechanisms at two different times. Conflating them is the classic compose trap.**

| | `${VAR}` interpolation | `env_file:` |
|---|---|---|
| Resolved by | Compose CLI, at **parse/config time** | Docker daemon, at **container start** |
| Source | shell environment + the `.env` in the **project directory** | the file named in the directive |
| Affects | the compose document text itself | the container's environment only |
| Missing-value behaviour | empty string, or abort with `${VAR:?msg}` | key simply absent |

They point at the same physical `backend/deploy/.env` here, which is why the current setup works — but that is a **coincidence of location**, not a shared mechanism. Compose v2 derives the project directory from the first `-f` file's directory, so `docker compose -f backend/deploy/docker-compose.yml ...` from the repo root still finds `backend/deploy/.env`. Worth a comment in the file, since the coincidence is load-bearing and invisible.

**Per-variable assignment:**

| Variable | compose:line | Mechanism | Why |
|---|---|---|---|
| `POSTGRES_PASSWORD` | 5 | **interpolation** `${POSTGRES_PASSWORD:?...}` | the `postgres` service has **no** `env_file:` (verified, `docker-compose.yml:2-11`). Interpolation is the only mechanism available. |
| `MSSQL_SA_PASSWORD` | 17 | **interpolation** | same — `sqlserver` has no `env_file:` (`:13-20`) |
| CardVault's `ConnectionStrings__Postgres` | 56 | **interpolation of the password substring only** | see below |
| everything else (`Jwt__*`, `Vault__*`, `Tokenization__Secret`, ...) | — | **`env_file: .env`**, unchanged (`:49`, `:68`, `:82`) | already injected wholesale; no compose-level change needed |

**The CardVault override — only the password is secret.** Replace the fully-inline value with a composed one:

```yaml
      - ConnectionStrings__Postgres=Host=postgres;Port=5432;Database=cardvault;Username=postgres;Password=${POSTGRES_PASSWORD:?set POSTGRES_PASSWORD in backend/deploy/.env}
```

Host, port, database and username are not secrets and stay literal. This reuses the **same** `POSTGRES_PASSWORD` variable that the `postgres` container itself consumes — which is correct by construction, because it is the same server's password. Today those are two independent literals that merely happen to agree: a latent drift bug where changing one silently breaks CardVault. One variable removes the drift class entirely.

- **Rejected — a new `.env` key for CardVault's whole connection string** (e.g. `CARDVAULT_POSTGRES_CONNECTION`). Adds a third Postgres-ish key to the very file whose duplicate-key confusion is being fixed, and resurrects a `CARDVAULT_*` name PR4 just retired.
- **Rejected — reusing the design-time `ConnectionStrings__CardVaultPostgres` for the runtime override.** Superficially elegant, actually wrong: design-time is host-side (`Host=localhost;Port=15432`), runtime is container-side (`Host=postgres;Port=5432`). One variable cannot hold both. See ADR-4.

**Mandatory-variable syntax.** `${VAR:?message}` makes Compose abort at parse time with the message when `VAR` is unset **or empty**. `${VAR?message}` aborts only when unset, letting an empty value through — and an empty `POSTGRES_PASSWORD` is exactly the silent passwordless-database outcome being prevented. Use `:?`.

**Duplicate-key fix in `.env.example`.** `ConnectionStrings__Postgres=__REPLACE_ME__` appears twice — once under CardVault, once under IsoSwitch. Last-wins in dotenv, so the CardVault occurrence is dead documentation.

With the composed override above, CardVault no longer needs that key at all. **Delete the CardVault-section occurrence, keep IsoSwitch's single declaration, and add an inline comment** stating that CardVault's runtime value is assembled in `docker-compose.yml` from `POSTGRES_PASSWORD` and does not come from this key. The runtime key is **not** renamed — that remains out of scope.

`.env.example` additions (PR6): `POSTGRES_PASSWORD`, `MSSQL_SA_PASSWORD`, and the three ADR-4 design-time keys, with a comment noting that design-time uses host-side addressing (`localhost:15432`) unlike the runtime values.

---

## ADR-8 — Pre-existing full-tree findings (SCOPE DELTA — needs acknowledgement)

**This ADR exists because the proposal's central PR7 premise is false as written.**

The proposal asserts PR7 "goes green from the moment it exists" because PR1-PR6 purge every occurrence. That holds only for the **12 inventoried** locations. A full-tree scan sees files a diff scan never has, and the current ruleset matches **~14 additional lines** in the working tree. I enumerated them by running the exact `inline-connection-string-password` regex over the tree:

| Class | Locations | Inventoried? | Disposition |
|---|---|---|---|
| Test fixture connection strings | `CardVaultWebApplicationFactory.cs:59`, `AddPinKdfColumnsMigrationTests.cs:26` (both `Password=test`) | **No** | genuine non-secrets, never connected → reviewed fingerprints |
| This change's own planning docs | `sec9-config-secret-remediation/proposal.md:36,39,45`, `exploration.md:18-25` | **No** | prose quoting the leak being removed → Rule A `\.md$` allowlist |
| Archived spec/design prose | `archive/2026-06-12-ola0-security-hardening/{spec.md:305,proposal.md:23,design.md:89}`, `archive/2026-07-24-phase0-security-blockers/{tasks.md:35,specs/.../spec.md:18}` | **No** | same class → `\.md$` |
| Vendored third-party skill docs | `.agents/skills/dotnet-core-expert/references/cloud-native.md:54` | **No** | not authored here → `\.md$` |
| Frontend false positives | `login.component.ts:435,447` | **No** | rule defect → fixed by Rule A's tightening (ADR-5), **not** allowlisted |

**And the existing `.gitleaksignore` does not carry over.** Both entries are commit-scoped git-mode fingerprints (`<commit>:<file>:<rule>:<line>`, per its own header at `:2`). Working-tree mode emits `<file>:<rule>:<line>` — no commit component. So `AddPinKdfColumnsMigrationTests.cs:26` is **not** currently exempt under the new mode, and entry `:16` is doubly dead: it names the **pre-archive** path `openspec/changes/phase0-security-blockers/...`, while the file now lives at `openspec/changes/archive/2026-07-24-phase0-security-blockers/...` and is scanned at its new path. This is uncertainty #6 in ADR-6 and must be confirmed empirically.

**Decision: a bounded triage folded into PR6, using two instruments and deliberately not a third.**

1. **Rule-level `paths` allowlist for `\.md$` on Rule A** handles the prose class in one line. Every markdown match in the tree is documentation *about* the remediation, and this class recurs on every future security spec — fingerprinting it means permanent churn. **Residual gap, stated so it is a decision and not a silent narrowing:** a live connection-string password pasted into a markdown file would evade Rule A. It remains subject to the ~150 default gitleaks rules (`[extend] useDefault = true`) and to review. Per this change's own structural rule, the spec must record this explicitly.
2. **Reviewed fingerprints in `.gitleaksignore`** for the small set of real code-file exceptions (the two `Password=test` test fixtures), regenerated in working-tree format, each with the existing file's comment convention explaining why it is not a secret.
3. **Rejected — `--baseline-path`.** gitleaks can snapshot current findings and report only new ones. Mechanically ideal, and exactly the wrong instrument here: it is a blanket "accept everything currently present" that would make the gate green without anyone reading what it accepted. That is *trusting the mechanism instead of re-deriving the requirement* — the precise failure this change exists to correct. An explicit per-finding reviewed fingerprint costs more, and that is the point.
4. **Rejected — path-allowlisting `openspec/` or `.agents/`.** Broader than needed once `\.md$` covers the class, and `openspec/` is where future real specs live.

**Scope acknowledgement.** ADR-8 and Rule A's tightening are **not in the proposal's PR6 description**. They are not optional: without them PR7's success criterion ("a full-tree run exits 0") is unreachable, and PR6's own diff would be flagged by the existing PR-diff scan. Estimated addition to PR6: ~40-60 lines across `.gitleaks.toml` and `.gitleaksignore`. Revised PR6 estimate ~190-250 lines, still inside the 400-line budget. **Flagged for orchestrator/user acknowledgement rather than silently absorbed.**

**Apply-time prerequisite.** Exact fingerprints cannot be authored from memory — they must come from a real full-tree run against the widened ruleset. PR6 therefore needs a working gitleaks binary (Docker) in the apply environment. If unavailable, PR6 stalls the same way it stalls on the `.env*` permission blocker.

---

## ADR-9 — Test strategy per slice under strict TDD

**Context — the known hazard.** Six files use `Assert.Throws<OptionsValidationException>(() => factory.CreateClient())`: `CardVault.Tests/Security/{ConnectionStringFailFastTests,StartupSecretValidationTests}.cs`, `IsoSwitch.Tests/Security/{AdminApiKeyStartupTests,TcpIsoClientTlsStartupTests,StartupSecretValidationTests}.cs`, `IsoAudit.Tests/Security/StartupSecretValidationTests.cs`. Under CI's concurrent Release load these intermittently surface `ObjectDisposedException` from `WebApplicationFactory`'s disposal path instead of the validation exception. ADR-2 and ADR-3 add four to six more startup validators — landing straight into that pattern.

**Decision: split the assertion into two layers, so the brittle harness carries as little of the contract as possible.**

**Layer 1 — validator unit tests (primary; fully deterministic, no host).** Instantiate the validator and call it directly:

```csharp
var result = new RequiredConnectionStringsOptionsValidator()
    .Validate(null, new RequiredConnectionStringsOptions { Postgres = "" });

result.Failed.Should().BeTrue();
result.FailureMessage.Should().Contain("ConnectionStrings:Postgres");
```

This is where the SEC-9 contract actually lives — *the message names the missing key*. No web host, no DI container, no disposal race, milliseconds per case. Every branch (each missing key, each forbidden placeholder for `KafkaSigningOptionsValidator`, the all-present success path) is covered here. This is also the natural RED-first artifact for strict TDD: the validator class does not exist yet, so the test fails to compile, then passes.

**Layer 2 — one wiring test per service (integration; tolerant).** Layer 1 cannot prove `Program.cs` actually registered the validator. Exactly **one** test per new validator asserts the service refuses to start, via a new per-test-project helper:

```
CardVault.Tests/Infrastructure/StartupValidationAssert.cs
IsoSwitch.Tests/Infrastructure/StartupValidationAssert.cs
IsoAudit.Tests/Infrastructure/StartupValidationAssert.cs
```

The helper (a) invokes the client factory, (b) walks the full exception chain including `AggregateException.InnerExceptions`, (c) passes when an `OptionsValidationException` appears anywhere in that chain, and (d) when the only exception is `ObjectDisposedException` with no `OptionsValidationException` in the chain, **retries the whole construction once** before failing with a diagnostic naming what it actually caught.

**The tradeoff, stated plainly.** A retry is not free — it can mask a genuinely intermittent product bug. It is chosen because the alternatives are worse:

- **Rejected — accept `ObjectDisposedException` as a pass.** This is the tempting one-line fix and it is exactly wrong. It degrades the assertion from "startup failed *because of options validation*" to "startup failed *somehow*" — the test would then pass if startup broke for a completely unrelated reason. That is a control narrower than the requirement it claims to enforce: this change's entire thesis, re-committed in the test layer. Refused.
- **Rejected — retrofit all six existing files now.** Out of scope, inflates every slice, and mixes a flakiness refactor into a security remediation. The helper is written so that refactor is a later mechanical change; documented as a follow-up.
- **Rejected — drop Layer 2 entirely.** Then nothing proves `ValidateOnStart()` was wired, and a missing `Program.cs` line ships green.

Per-project duplication of the helper follows ADR-2's reasoning: there is no shared test library, and none is worth creating for ~25 lines.

**Per-slice test plan:**

| Slice | RED-first artifact | Notes |
|---|---|---|
| PR1 | discovery + floor/superset tests in `CommittedConfigSecretShapeTests` | RED demonstrated locally against `main` @ `41a5bfa` and recorded in the PR body; the same PR's purge turns it green. A red test cannot merge in a stacked-to-main chain. |
| PR2 | IsoSwitch `RequiredConnectionStringsOptionsValidator` unit tests + 1 wiring test | fails to compile first |
| PR3 | same for IsoAudit + a test asserting no in-code fallback remains | |
| PR4 | 3 factory tests: env var unset → `InvalidOperationException` whose message names the required key **and** its predecessor | pure unit tests, no host, no flakiness — clear the env var, assert, restore |
| PR5 | `KafkaSigningOptionsValidator` unit tests incl. `dev-signing-secret` rejection + 1 wiring test per service | **must** add `Kafka:SigningSecret` to all affected web-application factories (ADR-3) or unrelated suites go red |
| PR6 | no .NET test — acceptance is an empirical full-tree gitleaks run over the widened ruleset, recorded in the PR body | ADR-5's match matrix is the checklist |
| PR7 | acceptance is the CI job itself passing on a clean tree | |

**Baseline.** Re-establish `dotnet test backend/CardSwitchPlatform.sln` on `main` @ `41a5bfa` before the first edit. The 705/705 figure dates from 2026-07-24 and was not re-verified in explore, propose, or this phase. A pre-existing failure must not be attributed to this change.

---

## File layout

| Path | Slice | Action |
|---|---|---|
| `CardVault.Tests/Security/CommittedConfigSecretShapeTests.cs` | PR1 | modify — discovery + floor/superset |
| `IsoSwitch.Api/appsettings.json:43`, `IsoAudit.Api/appsettings{,.Development}.json:3` | PR1 | empty the connection strings |
| `IsoSwitch.Api/Security/RequiredConnectionStringsOptions{,Validator}.cs` | PR2 | new |
| `IsoSwitch.Api/Program.cs` (near `:141-144`) | PR2 | register + `ValidateOnStart()` |
| `IsoAudit.Api/Security/RequiredConnectionStringsOptions{,Validator}.cs` | PR3 | new |
| `IsoAudit.Api/Program.cs:22-26` (register), `:62-63` (drop `??`) | PR3 | modify |
| `IsoSwitchDbContextFactory.cs`, `CardVaultDbContextFactory.cs`, `IdentityAppDbContextFactory.cs` | PR4 | rename + throw |
| `{CardVault,IsoSwitch}.Api/Security/KafkaSigningOptions{,Validator}.cs` | PR5 | new |
| `CardVault.Api/Program.cs:313-314`, `IsoSwitch.Api/Program.cs:169` | PR5 | register + `ValidateOnStart()` |
| `{IsoSwitch,CardVault}.Api/appsettings.Development.json` (`:26`, `:9`) | PR5 | empty `Kafka:SigningSecret` |
| `{CardVault,IsoSwitch,IsoAudit}.Tests/Infrastructure/*WebApplicationFactory.cs` | PR2/3/5 | add `UseSetting("Kafka:SigningSecret", ...)` |
| `{CardVault,IsoSwitch,IsoAudit}.Tests/Infrastructure/StartupValidationAssert.cs` | PR2/3/5 | new |
| `.gitleaks.toml` | PR6 | Rule A tighten + Rules B/C + `${}` allowlist + stale comment + drop `docs/env.example` path |
| `.gitleaksignore` | PR6 | regenerate in working-tree fingerprint format |
| `backend/deploy/docker-compose.yml:5,17,56` | PR6 | `${VAR:?msg}` interpolation |
| `backend/deploy/.env.example` | PR6 | add 5 vars, fix duplicate key — **permission-gated** |
| `docs/env.example` | PR6 | delete |
| `SECURITY.md:40` | PR6 | `backend/.env.example` → `backend/deploy/.env.example` (verified: line 40) |
| `.github/workflows/ci.yml:12-26` | PR7 | CLI full-tree blocking job; drop `fetch-depth: 0` |

---

## One-way doors

**None.** No history rewrite, no key rotation, no schema change, no data migration, no production topology change. Every slice reverts cleanly; PR1's and PR3's reverts restore weaker defaults, so forward-fix is preferred there.

## Assumptions requiring validation

| # | Assumption | How to validate |
|---|---|---|
| 1 | gitleaks v8.30.1 subcommand/flags/fingerprint format (ADR-6, all 7 items) | `gitleaks version`, `gitleaks dir --help`, one scratch scan — **before** PR7 claims green |
| 2 | Per-rule `[rules.allowlist]` and rule-level `path` are supported in v8.30.1 | empirical full-tree run in PR6 |
| 3 | `ghcr.io/gitleaks/gitleaks:v8.30.1` tag exists | `docker manifest inspect`; fallback `zricethezav/gitleaks:v8.30.1` |
| 4 | Whether Phase 0's git-history scrub ran (decides only whether git mode was *ever* viable; working-tree mode is correct either way) | `git log --all -S '<purged k1 value>'` |
| 5 | 705/705 baseline on `main` @ `41a5bfa` | full suite run before the first edit |
| 6 | Requiring `Kafka:SigningSecret` non-empty breaks no unrelated suite | run after PR5's factory updates |
| 7 | `backend/deploy/.env.example` current byte content — this phase could **not** read it (`.env*` is permission-denied, and no shell was available for `git show`) | read it once the permission narrowing is in place, before editing |
| 8 | The `.env*` permission narrowing is actually in place when PR6 starts | attempt a `Read` on the file at the start of PR6; stop if denied |

## Deliberately deferred, documented

1. Unify CardVault's runtime `ConnectionStrings:Postgres` key (proposal non-goal).
2. A full `KafkaOptions` type replacing raw `kafka["..."]` indexing (ADR-3).
3. Move IsoAudit's pre-`Run()` DB bootstrap into the already-defined-but-unregistered `DbMigrateWorker` so `ValidateOnStart` fires first (ADR-2).
4. Retrofit the six existing `Assert.Throws<OptionsValidationException>` files onto `StartupValidationAssert` (ADR-9).
5. `frontend/src/app/features/auth/login.component.ts:439` hardcodes `password: ['Admin1234!']` as a demo form default. Not a gitleaks match under any rule here, and frontend work is out of scope — but it is a committed credential-shaped literal and a reviewer will ask about it.
