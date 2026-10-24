# Decision Log

Short architecture decision records. Each decision states the context, the choice and the consequence. New decisions are added at the bottom and never rewritten; a changed decision gets a new entry that supersedes the old one.

| ID | Decision | Status |
|---|---|---|
| ADR-001 | PostgreSQL only | Accepted |
| ADR-002 | One API with three roles | Accepted |
| ADR-003 | Separate User and DriverProfile (composition) | Accepted |
| ADR-004 | Ownership checks instead of row-level security | Accepted |
| ADR-005 | First accept wins in version 1 | Accepted |
| ADR-006 | Polling in version 1, SignalR in version 2 | Accepted |
| ADR-007 | Haversine distance behind an interface | Accepted |
| ADR-008 | Simulated payments, no card data | Accepted |
| ADR-009 | Fake email sender behind an interface | Accepted |
| ADR-010 | Modular monolith, controllers | Accepted |
| ADR-011 | Driver approval and insurance gate | Accepted |
| ADR-012 | DTOs in feature folders | Accepted |

---

### ADR-001: PostgreSQL only
**Context:** The project needs relational integrity and a free, widely used database that runs in Docker.
**Decision:** Use PostgreSQL for all data. No second datastore in version 1.
**Consequence:** One migration history, simple local setup. Caching or geospatial extensions can be added later if needed.

### ADR-002: One API with three roles
**Context:** Customers, drivers and admins share users, rides and pricing.
**Decision:** Build one API. Roles (Customer, Driver, Admin) are claims in the JWT. One email address maps to one role.
**Consequence:** Simple deployment and shared code. Role rules must be tested carefully.

### ADR-003: Separate User and DriverProfile
**Context:** Drivers have extra data (approval, location, licence).
**Decision:** Use composition: `User` holds identity, `DriverProfile` holds driver-only data in a 1:1 relationship. No inheritance.
**Consequence:** Fewer table and query surprises; customers carry no empty driver columns.

### ADR-004: Ownership checks instead of row-level security
**Context:** Users must not read each other's data.
**Decision:** Take the user id from token claims, filter every query by it, return 404 for foreign resources, and centralise the "visible rides" query.
**Consequence:** Authorisation lives in application code and must be covered by tests (customer A requesting customer B's ride).

### ADR-005: First accept wins in version 1
**Context:** Several drivers may try to accept the same ride.
**Decision:** Enforce with one conditional update (or a concurrency token). The loser receives 409.
**Consequence:** Simple and correct. Fairness and nearest-first matching arrive with one-at-a-time offers in version 2.

### ADR-006: Polling now, SignalR later
**Context:** Clients need status changes.
**Decision:** Polling endpoints in version 1; SignalR hub in version 2.
**Consequence:** Version 1 stays simple; the status model does not change when SignalR is added.

### ADR-007: Haversine distance behind IDistanceService
**Context:** Fares depend on distance, and a routing service is a later upgrade.
**Decision:** Version 1 uses straight-line (Haversine) distance behind an interface.
**Consequence:** Estimates are optimistic for winding roads; swapping the implementation later needs no change to callers.

### ADR-008: Simulated payments, never card data
**Context:** Real payments bring compliance and security obligations.
**Decision:** Version 1 records in-car payments (cash or debit machine). Online methods arrive in later versions through a provider interface. The API never receives or stores card numbers.
**Consequence:** Safe to publish; payment logic is replaceable.

### ADR-009: Fake email sender
**Context:** Notifications are needed but SMTP setup is not essential for the MVP.
**Decision:** `IEmailSender` with a logging implementation now and an SMTP implementation in version 2.
**Consequence:** Notification code is real; delivery is not.

### ADR-010: Modular monolith with controllers
**Context:** Microservices add operational cost that is not justified yet.
**Decision:** One deployable API using controllers, organised by feature. Minimal APIs remain a possible later exercise; extraction of a module is an optional version 3 experiment.
**Consequence:** Faster development and easier debugging.

### ADR-011: Driver approval and insurance gate
**Context:** Safety and trust.
**Decision:** Drivers self-register as Pending. Only approved drivers with an active vehicle and unexpired insurance can go online.
**Consequence:** An expiry check runs whenever a driver goes online; version 2 adds automatic offline handling.

### ADR-012: DTOs in feature folders
**Context:** Entities must not leak through the API.
**Decision:** DTOs live in subfolders by feature (Auth, Rides, Drivers, Vehicles, Admin).
**Consequence:** Easy navigation and clear ownership of request and response shapes.

---

## Open decisions

These need an answer from the project owner before the related feature is built.

| # | Question | Blocks | Current proposal |
|---|---|---|---|
| OD-1 | Confirm the ride state table and who may make each move | Features 1.032 to 1.036 (accept, start, complete, cancel) | As drawn in [02-mvp.md](02-mvp.md#7-ride-lifecycle-proposed) |
| OD-2 | Cancellation rules after acceptance (fees, driver cancel behaviour) | Features 1.035 and 1.036 | No fee in version 1; driver cancel returns the ride to Requested |
| OD-3 | Fare numbers: base fare, price per km, minimum fare | Features 1.015 and 1.023 | Use placeholder values; check local meter rates or ask a local driver |
| OD-4 | Which vehicle types exist | Version 2 multiple types | Single type in version 1 |
| OD-5 | Can a driver have two active rides | Feature 1.032 | No; one active ride per driver |
| OD-6 | Who records in-car payment | Features 1.039 to 1.040 | The driver records it at the end of the ride |
