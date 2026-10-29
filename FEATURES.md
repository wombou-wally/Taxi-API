# Feature List

> The single source of truth for what gets built, in what order, and what depends on what.
> Work from top to bottom. Never start a feature until everything in its **Depends on** list is ticked.

## How to use this list

1. Open the first unticked feature whose dependencies are all done.
2. Create a branch named after it, for example `feature/1.005-customer-registration`.
3. Build it until its **Done when** line is true and the Definition of Done below is met.
4. Merge the branch, tick the box here, and update the progress log at the bottom.
5. Commit the tick with a message such as `docs: complete 1.005`.

**Size points:** S = 1 (about an evening), M = 2 (a couple of sessions), L = 3 (several sessions, hardest part of the stage). Points are for the burn-down only; they are estimates, not promises.

## Summary

| Version | Theme                | Features |  Points |
| ------- | -------------------- | -------: | ------: |
| 1       | MVP                  |       48 |      76 |
| 2       | Real-time and polish |       14 |      30 |
| 3       | Growth               |        9 |      26 |
|         | **Total**            |   **71** | **132** |

## Definition of Done

A feature is done only when all of these are true:

- [ ] It behaves as the **Done when** line says, checked by hand and in Scalar/Swagger
- [ ] Input is validated and errors return `ProblemDetails`
- [ ] The endpoint has the correct role restriction, and ownership is checked in the query
- [ ] Responses use DTOs, never entities
- [ ] Any migration file was read before it was applied
- [ ] No secrets in source control
- [ ] Tests added where the feature has rules worth protecting
- [ ] Merged to `main` and ticked in this file

## Legend

Roles: **Customer**, **Driver**, **Admin**, **All** (any logged-in user), **System** (background or internal), **Dev** (engineering task). Story links point to [03-user-stories.md](docs/03-user-stories.md).

---

## Version 1: MVP

### A. Foundation: project, database, authentication

- [DONE] **Feature 1.001** — Create repository, solution, .gitignore and README skeleton  
  Role: Dev · Size: S (1) · Depends on: none · Stories: US-22  
  _Done when:_ Solution builds; .gitignore committed before the first commit; first push to GitHub.
- [DONE] **Feature 1.002** — Add EF Core with PostgreSQL (Npgsql) and secrets in User Secrets  
   Role: Dev · Size: S (1) · Depends on: 1.001 · Stories: US-22  
   _Done when:_ App connects to a local PostgreSQL; no connection string or key in source control.
- [DONE] **Feature 1.003** — Global exception handler returning ProblemDetails  
   Role: Dev · Size: S (1) · Depends on: 1.001 · Stories: US-02  
   _Done when:_ Unhandled exceptions return a consistent problem JSON; no stack traces leak.
- [DONE] **Feature 1.004** — User entity, Role enum and first migration  
   Role: Dev · Size: M (2) · Depends on: 1.002 · Stories: US-01  
   _Done when:_ Users table exists with unique email index; migration file read before it is applied.
- [DONE] **Feature 1.005** — Customer registration with DTO validation and PasswordHasher  
   Role: Customer · Size: M (2) · Depends on: 1.004, 1.003 · Stories: US-01  
   _Done when:_ Register returns 201; duplicate email returns 409; password never stored in plain text.
- [DONE] **Feature 1.006** — Login endpoint issuing a JWT with id and role claims  
   Role: All · Size: M (2) · Depends on: 1.005 · Stories: US-02  
   _Done when:_ Valid login returns a token; wrong credentials return 401; key comes from User Secrets.
- [DONE] **Feature 1.007** — Role-based authorization (Customer, Driver, Admin)  
   Role: All · Size: S (1) · Depends on: 1.006 · Stories: US-02, US-21  
   _Done when:_ Wrong role returns 403; missing token returns 401; user id read from claims only.
- [DONE] **Feature 1.008** — Seed the first admin at startup from configuration  
       Role: Admin · Size: S (1) · Depends on: 1.004, 1.006 · Stories: US-05  
       _Done when:_ Admin exists after first run; credentials come from User Secrets, not code.
- [DONE] **Feature 1.009** — View and update my own profile (GET/PUT /me)  
       Role: All · Size: S (1) · Depends on: 1.007 · Stories: US-03  
       _Done when:_ A user reads and edits only their own name and phone.
- [DONE] **Feature 1.010** — Tests: registration, login and role checks  
       Role: Dev · Size: M (2) · Depends on: 1.007, 1.009 · Stories: US-01, US-02  
       _Done when:_ Integration tests cover register, login, 401 and 403.

### B. Drivers and vehicles

- [DONE] **Feature 1.011** — DriverProfile entity (1:1 with User) and migration  
       Role: Dev · Size: M (2) · Depends on: 1.004 · Stories: US-04  
       _Done when:_ DriverProfile table exists with ApprovalStatus, IsOnline, location fields and licence number.
- [DONE] **Feature 1.012** — Driver self-registration (status Pending)  
       Role: Driver · Size: M (2) · Depends on: 1.011, 1.005 · Stories: US-04  
       _Done when:_ Registering as a driver creates User + DriverProfile in one transaction.
- [DONE] **Feature 1.013** — Admin lists pending drivers  
       Role: Admin · Size: S (1) · Depends on: 1.012, 1.008, 1.007 · Stories: US-05  
       _Done when:_ Only an admin sees the list; response uses a DTO.
- [DONE] **Feature 1.014** — Admin approves or rejects a driver  
       Role: Admin · Size: S (1) · Depends on: 1.013 · Stories: US-05  
       _Done when:_ Status changes persist; a rejected driver cannot go online.
- [DONE] **Feature 1.015** — VehicleType entity with seeded single type and prices  
       Role: Dev · Size: S (1) · Depends on: 1.002 · Stories: US-18  
       _Done when:_ One vehicle type with BaseFare, PricePerKm and MinimumFare exists after migration.
- [ ] **Feature 1.016** — Admin edits vehicle type prices  
       Role: Admin · Size: S (1) · Depends on: 1.015, 1.007, 1.008 · Stories: US-18  
       _Done when:_ Valid update persists; negative values rejected with 400.
- [ ] **Feature 1.017** — Vehicle entity (plate, insurance number and expiry) and migration  
       Role: Dev · Size: M (2) · Depends on: 1.011, 1.015 · Stories: US-06  
       _Done when:_ Vehicle table exists with foreign keys to driver and vehicle type.
- [ ] **Feature 1.018** — Driver adds and edits their own vehicle  
       Role: Driver · Size: M (2) · Depends on: 1.017, 1.014 · Stories: US-06, US-21  
       _Done when:_ A driver reaches only their own vehicles; others return 404.
- [ ] **Feature 1.019** — Driver goes online or offline with eligibility checks  
       Role: Driver · Size: M (2) · Depends on: 1.018 · Stories: US-07  
       _Done when:_ Refused when not approved, no active vehicle or insurance expired.
- [ ] **Feature 1.020** — Driver updates current location  
       Role: Driver · Size: S (1) · Depends on: 1.019 · Stories: US-08  
       _Done when:_ Latitude and longitude validated and stored with LocationUpdatedAt.

### C. Distance and pricing

- [ ] **Feature 1.021** — IDistanceService with Haversine implementation  
       Role: Dev · Size: S (1) · Depends on: 1.001 · Stories: US-09  
       _Done when:_ Known city pairs return the expected kilometres within a small tolerance.
- [ ] **Feature 1.022** — Fare calculation service: max(MinimumFare, BaseFare + PricePerKm x km)  
       Role: Dev · Size: M (2) · Depends on: 1.015, 1.021 · Stories: US-09  
       _Done when:_ Calculator reads prices from the database, not constants.
- [ ] **Feature 1.023** — Unit tests for distance and fare calculation  
       Role: Dev · Size: S (1) · Depends on: 1.022 · Stories: US-09  
       _Done when:_ Tests cover zero distance, minimum fare and a long trip.
- [ ] **Feature 1.024** — Fare estimate endpoint  
       Role: Customer · Size: S (1) · Depends on: 1.022, 1.007 · Stories: US-09  
       _Done when:_ Returns distance and fare for a pickup and dropoff.

### D. Ride lifecycle

- [ ] **Feature 1.025** — Ride entity, RideStatus enum and migration  
       Role: Dev · Size: M (2) · Depends on: 1.004, 1.015, 1.017 · Stories: US-10  
       _Done when:_ Ride table exists with all timestamps, estimated and final fare.
- [ ] **Feature 1.026** — Central 'visible rides' query enforcing ownership  
       Role: Dev · Size: M (2) · Depends on: 1.025 · Stories: US-21, US-11  
       _Done when:_ One method filters by role: customer own, driver Requested + assigned, admin all.
- [ ] **Feature 1.027** — Customer requests a ride  
       Role: Customer · Size: M (2) · Depends on: 1.025, 1.024, 1.007 · Stories: US-10  
       _Done when:_ Ride saved as Requested with estimated fare; second open ride rejected with 409.
- [ ] **Feature 1.028** — Customer lists and views own rides  
       Role: Customer · Size: S (1) · Depends on: 1.026, 1.027 · Stories: US-11  
       _Done when:_ Another customer's ride id returns 404.
- [ ] **Feature 1.029** — Ride status endpoint suitable for polling  
       Role: Customer · Size: S (1) · Depends on: 1.028 · Stories: US-11  
       _Done when:_ Customer sees status changes within a normal polling interval.
- [ ] **Feature 1.030** — Tests: customer A cannot read customer B's ride  
       Role: Dev · Size: M (2) · Depends on: 1.028 · Stories: US-21  
       _Done when:_ Test fails if the ownership filter is removed.
- [ ] **Feature 1.031** — Driver lists available rides  
       Role: Driver · Size: S (1) · Depends on: 1.026, 1.019 · Stories: US-12  
       _Done when:_ Only Requested rides are shown, only to online approved drivers.
- [ ] **Feature 1.032** — Driver accepts a ride (first accept wins)  
       Role: Driver · Size: L (3) · Depends on: 1.031, 1.020 · Stories: US-13  
       _Done when:_ Single conditional update or concurrency token; loser gets 409; one active ride per driver.
- [ ] **Feature 1.033** — Driver starts a ride  
       Role: Driver · Size: S (1) · Depends on: 1.032 · Stories: US-14  
       _Done when:_ Only the assigned driver, only from Accepted; StartedAt set.
- [ ] **Feature 1.034** — Driver completes a ride and final fare is stored  
       Role: Driver · Size: M (2) · Depends on: 1.033, 1.022 · Stories: US-14  
       _Done when:_ Only from InProgress; CompletedAt and FinalFare set.
- [ ] **Feature 1.035** — Customer cancels a ride  
       Role: Customer · Size: M (2) · Depends on: 1.027, 1.032 · Stories: US-15  
       _Done when:_ Allowed from Requested or Accepted; blocked once InProgress.
- [ ] **Feature 1.036** — Driver cancels an accepted ride (returns it to Requested)  
       Role: Driver · Size: M (2) · Depends on: 1.032 · Stories: US-16  
       _Done when:_ Ride becomes available again and assignment is cleared.
- [ ] **Feature 1.037** — Admin lists all rides with status filter  
       Role: Admin · Size: S (1) · Depends on: 1.026, 1.008 · Stories: US-17  
       _Done when:_ Admin sees every ride; other roles get 403.
- [ ] **Feature 1.038** — Integration tests: full ride lifecycle and double accept  
       Role: Dev · Size: L (3) · Depends on: 1.034, 1.035, 1.036 · Stories: US-13, US-14  
       _Done when:_ Request, accept, start, complete passes; two simultaneous accepts yield one 409.

### E. Payments and notifications

- [ ] **Feature 1.039** — Payment entity and migration (method, status, amount)  
       Role: Dev · Size: S (1) · Depends on: 1.025 · Stories: US-19  
       _Done when:_ One payment per ride; no card data fields exist.
- [ ] **Feature 1.040** — Record in-car payment (cash or debit machine) on completion  
       Role: Driver · Size: M (2) · Depends on: 1.039, 1.034 · Stories: US-19  
       _Done when:_ Payment saved as Paid with method; cannot be recorded twice.
- [ ] **Feature 1.041** — IEmailSender with a fake logging implementation  
       Role: Dev · Size: S (1) · Depends on: 1.001 · Stories: US-20  
       _Done when:_ Interface registered in DI; fake writes recipient and subject to the log.
- [ ] **Feature 1.042** — Send notifications on ride accepted and completed  
       Role: System · Size: S (1) · Depends on: 1.041, 1.032, 1.034 · Stories: US-20  
       _Done when:_ Both events produce a log entry through IEmailSender.

### F. Quality and delivery

- [ ] **Feature 1.043** — OpenAPI documentation with Scalar or Swagger  
       Role: Dev · Size: S (1) · Depends on: 1.027 · Stories: US-22  
       _Done when:_ All endpoints documented with examples and authorization shown.
- [ ] **Feature 1.044** — Multi-stage Dockerfile for the API  
       Role: Dev · Size: M (2) · Depends on: 1.038 · Stories: US-22  
       _Done when:_ Image builds and runs on a clean machine.
- [ ] **Feature 1.045** — docker-compose with API and PostgreSQL  
       Role: Dev · Size: M (2) · Depends on: 1.044 · Stories: US-22  
       _Done when:_ docker compose up starts everything and applies migrations.
- [ ] **Feature 1.046** — GitHub Actions: build and test on every push  
       Role: Dev · Size: M (2) · Depends on: 1.038, 1.030 · Stories: US-22  
       _Done when:_ Green badge on the README.
- [ ] **Feature 1.047** — Final README: overview, architecture, setup, API tour  
       Role: Dev · Size: M (2) · Depends on: 1.045, 1.043 · Stories: US-22  
       _Done when:_ A stranger can clone and run the project from the README alone.
- [ ] **Feature 1.048** — Deploy the API and database to a hosting service  
       Role: Dev · Size: L (3) · Depends on: 1.045, 1.046 · Stories: US-22  
       _Done when:_ A public URL serves the API; secrets are set as environment variables.

---

## Version 2: Real-time and polish

### G. Real-time and dispatch

- [ ] **Feature 2.001** — SignalR hub for live ride status  
       Role: System · Size: L (3) · Depends on: 1.029 · Stories: US-23  
       _Done when:_ Customer and driver receive status changes without polling.
- [ ] **Feature 2.002** — RideOffer entity and one-at-a-time offering with timeout  
       Role: System · Size: L (3) · Depends on: 1.032, 1.020 · Stories: US-24  
       _Done when:_ Nearest eligible driver gets an exclusive offer for 10 to 15 seconds.
- [ ] **Feature 2.003** — NoDriverFound state and customer notification  
       Role: System · Size: M (2) · Depends on: 2.002 · Stories: US-25  
       _Done when:_ Ride ends in NoDriverFound when all offers expire.
- [ ] **Feature 2.004** — Background service marks drivers offline when location is stale  
       Role: System · Size: M (2) · Depends on: 1.020 · Stories: US-07  
       _Done when:_ Drivers with old LocationUpdatedAt are set offline automatically.

### H. Ratings, history and money

- [ ] **Feature 2.005** — Rating entity and endpoints  
       Role: Customer · Size: M (2) · Depends on: 1.034 · Stories: US-26  
       _Done when:_ One rating per party per ride; average calculated, not stored.
- [ ] **Feature 2.006** — Driver trip history and earnings summary  
       Role: Driver · Size: M (2) · Depends on: 1.034 · Stories: US-27  
       _Done when:_ Date-range query returns count and total using paging.
- [ ] **Feature 2.007** — Payment provider interface with sandbox implementation  
       Role: Customer · Size: L (3) · Depends on: 1.040 · Stories: US-29  
       _Done when:_ Sandbox card payment succeeds or fails; no card data touches the API.
- [ ] **Feature 2.008** — Multiple vehicle types and fare per type  
       Role: Customer · Size: M (2) · Depends on: 1.024, 1.016 · Stories: US-32  
       _Done when:_ Estimate returns one fare per active vehicle type.

### I. Administration and platform

- [ ] **Feature 2.009** — Admin statistics dashboard endpoint  
       Role: Admin · Size: M (2) · Depends on: 1.037, 2.006 · Stories: US-28  
       _Done when:_ Rides per day, active drivers and revenue returned.
- [ ] **Feature 2.010** — Suspend and reactivate users  
       Role: Admin · Size: M (2) · Depends on: 1.014 · Stories: US-31  
       _Done when:_ Suspended users cannot log in; drivers go offline.
- [ ] **Feature 2.011** — Real email sending via SMTP implementation  
       Role: System · Size: M (2) · Depends on: 1.042 · Stories: US-30  
       _Done when:_ Second IEmailSender implementation chosen by configuration.
- [ ] **Feature 2.012** — Refresh tokens with rotation  
       Role: All · Size: L (3) · Depends on: 1.006 · Stories: US-33  
       _Done when:_ Expired access token renewed without logging in; reuse detected.
- [ ] **Feature 2.013** — Rate limiting on login and ride requests  
       Role: System · Size: S (1) · Depends on: 1.006, 1.027 · Stories: US-02  
       _Done when:_ Excess requests return 429.
- [ ] **Feature 2.014** — Health checks and structured logging  
       Role: Dev · Size: S (1) · Depends on: 1.048 · Stories: US-28  
       _Done when:_ /health reports database status; logs are structured.

---

## Version 3: Growth

### J. Better maps and money

- [ ] **Feature 3.001** — Routing service for road distance and ETA  
       Role: System · Size: L (3) · Depends on: 1.021, 2.008 · Stories: US-34  
       _Done when:_ A second IDistanceService implementation replaces Haversine by configuration.
- [ ] **Feature 3.002** — Real payment providers (cards, Google Pay, Apple Pay, PayPal)  
       Role: Customer · Size: L (3) · Depends on: 2.007 · Stories: US-35  
       _Done when:_ Webhook confirms payment and updates the Payment.
- [ ] **Feature 3.003** — Promo codes  
       Role: Customer · Size: M (2) · Depends on: 1.022 · Stories: US-37  
       _Done when:_ Valid code reduces the fare once per customer.
- [ ] **Feature 3.004** — Surge pricing multipliers  
       Role: Admin · Size: L (3) · Depends on: 3.001, 2.009 · Stories: US-41  
       _Done when:_ Multiplier shown separately in the estimate.
- [ ] **Feature 3.005** — Scheduled rides  
       Role: Customer · Size: L (3) · Depends on: 2.002 · Stories: US-36  
       _Done when:_ Background job dispatches the ride before pickup time.

### K. Clients and scale

- [ ] **Feature 3.006** — React web client  
       Role: All · Size: L (3) · Depends on: 2.012, 2.001 · Stories: US-38  
       _Done when:_ Each role completes its main flow in the browser.
- [ ] **Feature 3.007** — React Native mobile apps  
       Role: All · Size: L (3) · Depends on: 3.006 · Stories: US-39  
       _Done when:_ Customer can book and driver can accept from a phone.
- [ ] **Feature 3.008** — Push notifications  
       Role: System · Size: L (3) · Depends on: 3.007, 1.042 · Stories: US-40  
       _Done when:_ Ride events arrive as push messages.
- [ ] **Feature 3.009** — Extract one module into a separate service (experiment)  
       Role: Dev · Size: L (3) · Depends on: 3.002, 2.014 · Stories: US-42  
       _Done when:_ Decision record explains what was split and what it cost.

---

## Burn-down log

Update this table whenever you finish a feature (or once a week). _Remaining_ = total points minus completed points.

Total points: **132** · Version 1 points: **76**

| Date  | Feature(s) completed | Points done (cumulative) | V1 remaining | Total remaining | Notes            |
| ----- | -------------------- | -----------------------: | -----------: | --------------: | ---------------- |
| start | —                    |                        0 |           76 |             132 | Project kick-off |
|       |                      |                          |              |                 |                  |
|       |                      |                          |              |                 |                  |
|       |                      |                          |              |                 |                  |
|       |                      |                          |              |                 |                  |
|       |                      |                          |              |                 |                  |
