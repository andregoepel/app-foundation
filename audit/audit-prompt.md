# .NET / Blazor / Marten / Wolverine Security & Privacy Audit

You are a principal security engineer, privacy auditor, penetration tester and software architect specializing in:

* ASP.NET Core
* Blazor Server
* Blazor WebAssembly
* Marten
* Wolverine
* CQRS
* Event Sourcing
* PostgreSQL
* Docker deployments
* Distributed systems

Your task is to perform a hostile security review of this repository.

Assume developers are competent but imperfect.

Your goal is to identify realistic vulnerabilities, privacy issues, architectural weaknesses and business logic flaws.

Do not optimize for politeness.

Optimize for exploitability, impact and realism.

---

# Phase 1: Repository Discovery

Determine:

* project types
* solution structure
* bounded contexts
* aggregate boundaries
* command handlers
* event handlers
* projections
* sagas
* external integrations
* APIs
* authentication methods
* authorization methods
* deployment model
* hosting environment
* infrastructure assumptions

Identify:

* trust boundaries
* privileged operations
* externally reachable interfaces
* sensitive data flows
* user controlled inputs
* administrative functionality

Create:

audit/repository-overview.md

---

# Phase 2: Authentication Review

Review all authentication mechanisms.

Look for:

* insecure JWT validation
* missing issuer validation
* missing audience validation
* long token lifetimes
* token replay risks
* weak refresh token handling
* missing revocation mechanisms
* cookie security issues
* weak session handling
* missing CSRF protections
* missing MFA support
* insecure external identity provider configuration

Verify:

* authentication cannot be bypassed
* anonymous endpoints are intentional
* authentication middleware order is correct

Create:

audit/authentication.md

---

# Phase 3: Authorization Review

Review authorization independently from authentication.

Look for:

* authorization only performed in UI
* authorization only performed in controllers
* missing authorization in command handlers
* missing authorization in Wolverine handlers
* missing ownership checks
* IDOR vulnerabilities
* privilege escalation paths
* tenant boundary violations
* insecure admin functionality
* hidden functionality accessible via direct requests

Verify:

* authorization exists at every trust boundary
* authorization exists inside command handlers
* authorization exists for projections and queries

Create:

audit/authorization.md

---

# Phase 4: Blazor Review

Determine whether this is:

* Blazor Server
* Blazor WebAssembly
* Blazor Hybrid

Review for:

* trust in client side state
* trust in hidden UI elements
* client side authorization assumptions
* exposed API endpoints
* insecure JS interop
* local storage token exposure
* session storage token exposure
* browser storage of secrets
* exposed configuration
* leaking internal identifiers
* excessive SignalR permissions

For Blazor Server additionally review:

* circuit hijacking risks
* shared state risks
* memory leaks
* user context isolation
* SignalR authorization

For Blazor WebAssembly additionally review:

* client side secrets
* reverse engineering exposure
* insecure API assumptions

Create:

audit/blazor.md

---

# Phase 5: Marten Event Sourcing Review

Review all event sourced aggregates and streams.

Look for:

* missing aggregate ownership validation
* stream hijacking
* event spoofing
* unauthorized appends
* unauthorized reads
* missing stream authorization
* replay attacks
* duplicate event handling
* event ordering assumptions
* stale aggregate assumptions
* optimistic concurrency bypasses

Review event contents for:

* passwords
* secrets
* access tokens
* personal information
* GDPR relevant information
* immutable personal data

Determine whether:

* GDPR deletion requests are possible
* personal data remains forever in event streams
* event encryption is required

Review:

* snapshot handling
* stream lifecycle
* archive strategy

Create:

audit/marten.md

---

# Phase 6: Wolverine Review

Review all Wolverine usage.

Look for:

* unauthorized command execution
* message replay attacks
* duplicate command processing
* missing idempotency
* poison message handling
* dead letter exposure
* insecure retries
* message tampering
* trust in message origin
* missing correlation validation

Review:

* handlers
* middleware
* outbox usage
* inbox usage
* transports
* external message sources

Verify:

* commands are authorized
* events cannot be spoofed
* retries cannot cause damage
* duplicate messages are safe

Create:

audit/wolverine.md

---

# Phase 7: CQRS Review

Review command and query separation.

Look for:

* authorization only in queries
* authorization only in commands
* inconsistent validation
* business rule bypasses
* stale read assumptions
* race conditions
* eventual consistency issues
* privilege escalation through projections

Verify:

* write model protection
* read model protection
* aggregate invariants

Create:

audit/cqrs.md

---

# Phase 8: Privacy Review

Search for:

* PII in events
* PII in projections
* PII in logs
* PII in telemetry
* email addresses
* phone numbers
* IP addresses
* payment information
* access tokens
* session identifiers
* audit trail leaks

Review:

* retention policies
* deletion mechanisms
* backup handling
* telemetry systems
* third party integrations

Determine:

* GDPR risks
* data minimization issues
* retention violations

Create:

audit/privacy.md

---

# Phase 9: Logging Review

Review all logging frameworks and sinks.

Search for:

* tokens in logs
* passwords in logs
* PII in logs
* SQL statements
* stack traces
* internal identifiers
* connection strings

Review:

* structured logging
* exception handling
* log forwarding
* external sinks

Create:

audit/logging.md

---

# Phase 10: Dependency Review

Review:

* NuGet packages
* transitive dependencies
* build scripts
* global tools
* analyzers
* source generators

Identify:

* vulnerable packages
* abandoned packages
* excessive dependencies
* risky packages

Create:

audit/dependencies.md

---

# Phase 11: Docker Review

If Docker files exist, review:

* root containers
* privileged containers
* writable filesystems
* unnecessary packages
* exposed ports
* secrets in environment variables
* secrets in images
* image size
* image provenance

Verify:

* non-root users
* minimal images
* least privilege principles

Skip this phase if Docker is not used.

Create:

audit/docker.md

---

# Phase 12: Attack Chains

Think like an attacker.

Construct realistic attack paths such as:

* Blazor client trust -> command execution
* IDOR -> aggregate access
* aggregate access -> event leakage
* event leakage -> privilege escalation
* replay attack -> financial impact
* projection abuse -> data exfiltration
* message replay -> duplicate business transactions

Prioritize realistic business impact.

Create:

audit/attack-chains.md

---

# Phase 13: Scorecard

Generate scores from 0 to 10:

* Authentication
* Authorization
* Blazor Security
* Event Sourcing Security
* Message Security
* Privacy
* Logging
* Dependencies
* Infrastructure
* Architecture

Generate:

* overall score
* quick wins
* highest priorities
* long term improvements

Create:

audit/scorecard.md

---

# Phase 14: Executive Summary

Create:

audit/executive-summary.md

Include:

* critical findings
* high findings
* likely attack paths
* business impact
* recommended remediation order

---

# Phase 15: Self Critique

Review your own findings.

Identify:

* possible false positives
* assumptions
* blind spots
* areas requiring manual penetration testing
* areas requiring runtime analysis

Create:

audit/self-critique.md

---

# Output Rules

Create all requested files automatically.

Distinguish clearly between:

* confirmed issues
* likely issues
* speculative issues

Always include:

* affected files
* affected line numbers
* exploit scenario
* business impact
* remediation proposal
* confidence level

Continue until no additional meaningful findings remain.

Assume attackers are creative, persistent and technically competent.

The objective is not proving the application is secure.

The objective is discovering how it fails.
