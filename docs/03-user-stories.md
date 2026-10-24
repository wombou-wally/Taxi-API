# User Stories

Stories describe what each person wants and why. Acceptance criteria use *Given / When / Then* so each one can become a test. Every story is linked to the features that deliver it in [FEATURES.md](../FEATURES.md).

## Personas

| Persona | Who they are | What they care about |
|---|---|---|
| **Customer** | Someone who needs a ride | Price certainty, a driver arriving, privacy |
| **Driver** | Self-registered, approved by an admin | Getting rides fairly, simple controls, earnings |
| **Admin** | Platform operator | Vetting drivers, pricing, seeing everything, support |

## Conventions

- **Format:** *As a [role], I want [goal], so that [benefit].*
- **Priority:** Must (version 1), Should (version 2), Could (version 3).
- One email address means one role.
- Missing and unauthorized resources both return 404 for rides and vehicles, so ids cannot be probed.

## Story map

| ID | Role | Story | Version | Priority | Features |
|---|---|---|---:|---|---|
| US-01 | customer | register with my email and a password | 1 | Must | 1.004, 1.005, 1.010 |
| US-02 | user (any role) | log in and receive a token | 1 | Must | 1.003, 1.006, 1.007, 1.010, 2.013 |
| US-03 | customer | view and update my profile | 1 | Must | 1.009 |
| US-04 | driver | register as a driver and submit my licence number | 1 | Must | 1.011, 1.012 |
| US-05 | admin | see pending drivers and approve or reject them | 1 | Must | 1.008, 1.013, 1.014 |
| US-06 | driver | add my vehicle and its insurance details | 1 | Must | 1.017, 1.018 |
| US-07 | driver | go online and offline | 1 | Must | 1.019, 2.004 |
| US-08 | driver | send my current location | 1 | Must | 1.020 |
| US-09 | customer | get a fare estimate before booking | 1 | Must | 1.021, 1.022, 1.023, 1.024 |
| US-10 | customer | request a ride | 1 | Must | 1.025, 1.027 |
| US-11 | customer | see the status and history of my rides | 1 | Must | 1.026, 1.028, 1.029 |
| US-12 | driver | see rides that are waiting for a driver | 1 | Must | 1.031 |
| US-13 | driver | accept a ride | 1 | Must | 1.032, 1.038 |
| US-14 | driver | start and complete a ride | 1 | Must | 1.033, 1.034, 1.038 |
| US-15 | customer | cancel my ride before it starts | 1 | Must | 1.035 |
| US-16 | driver | cancel a ride I accepted | 1 | Must | 1.036 |
| US-17 | admin | see every ride in the system | 1 | Must | 1.037 |
| US-18 | admin | edit the base fare, price per km and minimum fare | 1 | Must | 1.015, 1.016 |
| US-19 | customer | have my payment recorded when the ride ends | 1 | Must | 1.039, 1.040 |
| US-20 | customer or driver | be notified of ride events by email | 1 | Must | 1.041, 1.042 |
| US-21 | customer or driver | be sure other people cannot see my data | 1 | Must | 1.007, 1.018, 1.026, 1.030 |
| US-22 | developer or reviewer | start the whole system with one command | 1 | Must | 1.001, 1.002, 1.043, 1.044, 1.045, 1.046, 1.047, 1.048 |
| US-23 | customer or driver | see ride status change live | 2 | Should | 2.001 |
| US-24 | platform | offer a ride to one nearby driver at a time | 2 | Should | 2.002 |
| US-25 | customer | be told when no driver accepts | 2 | Should | 2.003 |
| US-26 | customer or driver | rate the other party after a ride | 2 | Should | 2.005 |
| US-27 | driver | see my completed trips and earnings | 2 | Should | 2.006 |
| US-28 | admin | see platform statistics | 2 | Should | 2.009, 2.014 |
| US-29 | customer | pay online with a card or wallet in a sandbox | 2 | Should | 2.007 |
| US-30 | customer or driver | receive real emails | 2 | Should | 2.011 |
| US-31 | admin | suspend a user or driver | 2 | Should | 2.010 |
| US-32 | customer | choose a vehicle type | 2 | Should | 2.008 |
| US-33 | user (any role) | stay logged in safely | 2 | Should | 2.012 |
| US-34 | customer | see road-based distance and arrival time | 3 | Could | 3.001 |
| US-35 | customer | pay with Google Pay, Apple Pay, Visa, debit or PayPal | 3 | Could | 3.002 |
| US-36 | customer | book a ride for a later time | 3 | Could | 3.005 |
| US-37 | customer | apply a promo code | 3 | Could | 3.003 |
| US-38 | customer, driver or admin | use a web application | 3 | Could | 3.006 |
| US-39 | customer or driver | use a mobile application | 3 | Could | 3.007 |
| US-40 | customer or driver | get push notifications | 3 | Could | 3.008 |
| US-41 | admin | apply higher prices at busy times | 3 | Could | 3.004 |
| US-42 | maintainer | split a part of the system into its own service | 3 | Could | 3.009 |

---

## Version 1: MVP

### US-01

**As a** customer, **I want** register with my email and a password, **so that** I can book rides.

*Priority:* Must · *Delivered by:* 1.004, 1.005, 1.010

**Acceptance criteria**

- Given a new email, when I register, then an account with role Customer is created and the password is stored hashed
- Given an email already in use, when I register, then I get a 409 problem response
- Given a weak or invalid input, when I register, then I get a 400 with field-level errors

### US-02

**As a** user (any role), **I want** log in and receive a token, **so that** I can use the parts of the API my role allows.

*Priority:* Must · *Delivered by:* 1.003, 1.006, 1.007, 1.010, 2.013

**Acceptance criteria**

- Given correct credentials, when I log in, then I receive a JWT containing my id and role
- Given wrong credentials, when I log in, then I get a 401 that does not reveal which part was wrong
- Given a token for one role, when I call an endpoint for another role, then I get a 403

### US-03

**As a** customer, **I want** view and update my profile, **so that** my name and phone number are correct.

*Priority:* Must · *Delivered by:* 1.009

**Acceptance criteria**

- Given I am logged in, when I call GET /me, then I see only my own data
- Given valid input, when I update my profile, then the change is saved and my email and role cannot be changed this way

### US-04

**As a** driver, **I want** register as a driver and submit my licence number, **so that** an admin can review me.

*Priority:* Must · *Delivered by:* 1.011, 1.012

**Acceptance criteria**

- Given valid input, when I register as a driver, then my account is created with ApprovalStatus Pending
- Given I am Pending or Rejected, when I try to go online, then I am refused

### US-05

**As a** admin, **I want** see pending drivers and approve or reject them, **so that** only vetted drivers take rides.

*Priority:* Must · *Delivered by:* 1.008, 1.013, 1.014

**Acceptance criteria**

- Given pending drivers exist, when I list them, then I see name, licence number and registration date
- Given a pending driver, when I approve, then ApprovalStatus becomes Approved
- Given a non-admin token, when calling these endpoints, then I get a 403

### US-06

**As a** driver, **I want** add my vehicle and its insurance details, **so that** the platform knows what I drive and that I am insured.

*Priority:* Must · *Delivered by:* 1.017, 1.018

**Acceptance criteria**

- Given I am approved, when I add a vehicle with plate and insurance expiry, then it is saved to my account
- Given a vehicle belongs to another driver, when I try to read or edit it, then I get a 404

### US-07

**As a** driver, **I want** go online and offline, **so that** I only receive rides when I want them.

*Priority:* Must · *Delivered by:* 1.019, 2.004

**Acceptance criteria**

- Given I am approved with a vehicle and valid insurance, when I go online, then IsOnline is true
- Given my insurance has expired, when I try to go online, then I am refused with a clear reason
- Given I am offline, when I list available rides, then I get none

### US-08

**As a** driver, **I want** send my current location, **so that** rides can be matched and priced by distance.

*Priority:* Must · *Delivered by:* 1.020

**Acceptance criteria**

- Given I am online, when I send latitude and longitude, then they are saved with a timestamp
- Given coordinates outside valid ranges, when I send them, then I get a 400

### US-09

**As a** customer, **I want** get a fare estimate before booking, **so that** I know the price before I commit.

*Priority:* Must · *Delivered by:* 1.021, 1.022, 1.023, 1.024

**Acceptance criteria**

- Given pickup and dropoff coordinates, when I ask for an estimate, then I get distance and fare using max(MinimumFare, BaseFare + PricePerKm x km)
- Given identical pickup and dropoff, when I ask, then the minimum fare is returned

### US-10

**As a** customer, **I want** request a ride, **so that** a driver can pick me up.

*Priority:* Must · *Delivered by:* 1.025, 1.027

**Acceptance criteria**

- Given valid pickup and dropoff, when I request a ride, then a Ride with status Requested and the estimated fare is created
- Given I already have an open ride, when I request another, then I get a 409

### US-11

**As a** customer, **I want** see the status and history of my rides, **so that** I know what is happening and what I have taken.

*Priority:* Must · *Delivered by:* 1.026, 1.028, 1.029

**Acceptance criteria**

- Given I own rides, when I list them, then I see only mine
- Given another customer's ride id, when I request it, then I get a 404 (not 403)
- Given my ride changes status, when I poll it, then I see the new status

### US-12

**As a** driver, **I want** see rides that are waiting for a driver, **so that** I can choose one to take.

*Priority:* Must · *Delivered by:* 1.031

**Acceptance criteria**

- Given I am online and approved, when I list available rides, then I see Requested rides
- Given a ride is already taken, when I list available rides, then it is not shown

### US-13

**As a** driver, **I want** accept a ride, **so that** the customer knows I am coming.

*Priority:* Must · *Delivered by:* 1.032, 1.038

**Acceptance criteria**

- Given a Requested ride, when I accept it, then it is assigned to me and its status becomes Accepted
- Given two drivers accept at the same moment, then exactly one succeeds and the other gets a 409
- Given I already have an active ride, when I accept another, then I get a 409

### US-14

**As a** driver, **I want** start and complete a ride, **so that** the trip and its final fare are recorded.

*Priority:* Must · *Delivered by:* 1.033, 1.034, 1.038

**Acceptance criteria**

- Given an Accepted ride that is mine, when I start it, then status becomes InProgress and StartedAt is set
- Given an InProgress ride that is mine, when I complete it, then status becomes Completed and FinalFare is stored
- Given a ride that is not mine, when I try to change it, then I get a 404

### US-15

**As a** customer, **I want** cancel my ride before it starts, **so that** I am not stuck with a trip I no longer need.

*Priority:* Must · *Delivered by:* 1.035

**Acceptance criteria**

- Given a Requested or Accepted ride that is mine, when I cancel, then status becomes Cancelled and CancelledAt is set
- Given an InProgress or Completed ride, when I cancel, then I get a 409

### US-16

**As a** driver, **I want** cancel a ride I accepted, **so that** the customer can be matched with someone else if I cannot come.

*Priority:* Must · *Delivered by:* 1.036

**Acceptance criteria**

- Given an Accepted ride that is mine, when I cancel, then it returns to Requested and my assignment is cleared

### US-17

**As a** admin, **I want** see every ride in the system, **so that** I can support customers and spot problems.

*Priority:* Must · *Delivered by:* 1.037

**Acceptance criteria**

- Given rides exist, when I list them, then I see all of them and can filter by status
- Given a customer or driver token, when calling the admin list, then I get a 403

### US-18

**As a** admin, **I want** edit the base fare, price per km and minimum fare, **so that** pricing can change without a deployment.

*Priority:* Must · *Delivered by:* 1.015, 1.016

**Acceptance criteria**

- Given valid values, when I update the vehicle type prices, then new estimates use them
- Given negative values, when I update, then I get a 400
- Given existing rides, when prices change, then their stored estimates are not altered

### US-19

**As a** customer, **I want** have my payment recorded when the ride ends, **so that** there is a clear record of what was paid.

*Priority:* Must · *Delivered by:* 1.039, 1.040

**Acceptance criteria**

- Given a completed ride, when the driver confirms the in-car payment method, then a Payment is stored with method and status Paid
- Given no card data, then the system never stores card numbers

### US-20

**As a** customer or driver, **I want** be notified of ride events by email, **so that** I do not have to keep checking the app.

*Priority:* Must · *Delivered by:* 1.041, 1.042

**Acceptance criteria**

- Given a ride is accepted or completed, when the event happens, then a fake email sender logs the message with recipient and subject

### US-21

**As a** customer or driver, **I want** be sure other people cannot see my data, **so that** my privacy is protected.

*Priority:* Must · *Delivered by:* 1.007, 1.018, 1.026, 1.030

**Acceptance criteria**

- Given customer A and customer B, when A requests B's ride by id, then A gets a 404
- Given any endpoint, the user id is taken from the token claims, never from the request body

### US-22

**As a** developer or reviewer, **I want** start the whole system with one command, **so that** I can try it without installing anything but Docker.

*Priority:* Must · *Delivered by:* 1.001, 1.002, 1.043, 1.044, 1.045, 1.046, 1.047, 1.048

**Acceptance criteria**

- Given Docker is installed, when I run docker compose up, then the API and PostgreSQL start and migrations are applied
- Given the README, then I can follow it from clone to first successful request

---

## Version 2: Real-time and polish

### US-23

**As a** customer or driver, **I want** see ride status change live, **so that** I do not have to refresh.

*Priority:* Should · *Delivered by:* 2.001

**Acceptance criteria**

- Given a ride changes status, then connected parties receive the change within about one second

### US-24

**As a** platform, **I want** offer a ride to one nearby driver at a time, **so that** drivers are not flooded and the closest gets first chance.

*Priority:* Should · *Delivered by:* 2.002

**Acceptance criteria**

- Given a Requested ride, then the nearest eligible driver gets an exclusive offer for 10 to 15 seconds
- Given the offer expires or is declined, then the next nearest driver is offered the ride

### US-25

**As a** customer, **I want** be told when no driver accepts, **so that** I can try again or do something else.

*Priority:* Should · *Delivered by:* 2.003

**Acceptance criteria**

- Given every eligible driver was offered the ride and none accepted, then the ride becomes NoDriverFound and I am notified

### US-26

**As a** customer or driver, **I want** rate the other party after a ride, **so that** quality stays high.

*Priority:* Should · *Delivered by:* 2.005

**Acceptance criteria**

- Given a completed ride, when I rate 1 to 5 once, then the rating is saved
- Given I was not part of the ride, when I try to rate it, then I get a 404

### US-27

**As a** driver, **I want** see my completed trips and earnings, **so that** I know how much I have made.

*Priority:* Should · *Delivered by:* 2.006

**Acceptance criteria**

- Given completed rides, when I request a summary for a date range, then I see trip count and total fares

### US-28

**As a** admin, **I want** see platform statistics, **so that** I can follow how the service is doing.

*Priority:* Should · *Delivered by:* 2.009, 2.014

**Acceptance criteria**

- Given data exists, when I open the dashboard endpoint, then I see rides per day, active drivers and revenue

### US-29

**As a** customer, **I want** pay online with a card or wallet in a sandbox, **so that** I do not need cash.

*Priority:* Should · *Delivered by:* 2.007

**Acceptance criteria**

- Given a payment provider token, when I pay, then the Payment is created with status Paid or Failed
- Given any request, then raw card numbers are never received or stored by this API

### US-30

**As a** customer or driver, **I want** receive real emails, **so that** notifications reach me outside the app.

*Priority:* Should · *Delivered by:* 2.011

**Acceptance criteria**

- Given SMTP settings, when a ride event happens, then an email is delivered through the real sender

### US-31

**As a** admin, **I want** suspend a user or driver, **so that** I can stop abuse quickly.

*Priority:* Should · *Delivered by:* 2.010

**Acceptance criteria**

- Given a suspended user, when they log in or call the API, then they are refused
- Given a suspended driver, then they are taken offline

### US-32

**As a** customer, **I want** choose a vehicle type, **so that** I can pick the service level I want.

*Priority:* Should · *Delivered by:* 2.008

**Acceptance criteria**

- Given several vehicle types, when I request an estimate, then I see a fare per type

### US-33

**As a** user (any role), **I want** stay logged in safely, **so that** I am not forced to log in every hour.

*Priority:* Should · *Delivered by:* 2.012

**Acceptance criteria**

- Given a valid refresh token, when I ask for a new access token, then I get one and the old refresh token is rotated

---

## Version 3: Growth

### US-34

**As a** customer, **I want** see road-based distance and arrival time, **so that** estimates are realistic.

*Priority:* Could · *Delivered by:* 3.001

**Acceptance criteria**

- Given a pickup and dropoff, then distance and ETA come from a routing service rather than straight-line distance

### US-35

**As a** customer, **I want** pay with Google Pay, Apple Pay, Visa, debit or PayPal, **so that** I can use my preferred method.

*Priority:* Could · *Delivered by:* 3.002

**Acceptance criteria**

- Given a supported method, when I pay, then the provider confirms and the Payment is updated via webhook

### US-36

**As a** customer, **I want** book a ride for a later time, **so that** I can plan ahead.

*Priority:* Could · *Delivered by:* 3.005

**Acceptance criteria**

- Given a future pickup time, then the ride is dispatched shortly before that time

### US-37

**As a** customer, **I want** apply a promo code, **so that** I get a discount.

*Priority:* Could · *Delivered by:* 3.003

**Acceptance criteria**

- Given a valid code, then the fare is reduced and the code usage is recorded

### US-38

**As a** customer, driver or admin, **I want** use a web application, **so that** I do not need the API directly.

*Priority:* Could · *Delivered by:* 3.006

**Acceptance criteria**

- Given the React app, then each role can complete its main flow in the browser

### US-39

**As a** customer or driver, **I want** use a mobile application, **so that** I can ride or drive from my phone.

*Priority:* Could · *Delivered by:* 3.007

**Acceptance criteria**

- Given the React Native app, then customers can book and drivers can accept rides

### US-40

**As a** customer or driver, **I want** get push notifications, **so that** I notice events when the app is closed.

*Priority:* Could · *Delivered by:* 3.008

**Acceptance criteria**

- Given a ride event, then a push notification is delivered to the registered device

### US-41

**As a** admin, **I want** apply higher prices at busy times, **so that** supply and demand stay balanced.

*Priority:* Could · *Delivered by:* 3.004

**Acceptance criteria**

- Given a surge multiplier is configured for a zone and time, then estimates include it and show it separately

### US-42

**As a** maintainer, **I want** split a part of the system into its own service, **so that** I can learn and evaluate the trade-offs.

*Priority:* Could · *Delivered by:* 3.009

**Acceptance criteria**

- Given the modular monolith, then one module can be extracted behind an interface with documented reasoning
