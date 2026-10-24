# Wni Taxi API

A ride-hailing backend built with **ASP.NET Core** and **PostgreSQL**. One API serves three roles: **Customer**, **Driver** and **Admin**.

> **Status:** planning complete, implementation starting. Progress is tracked in [FEATURES.md](FEATURES.md).

## Project documents

| Document | Purpose |
|---|---|
| [FEATURES.md](FEATURES.md) | Ordered feature list with dependencies, size points, versions 1 to 3 and a burn-down log |
| [docs/01-concept.md](docs/01-concept.md) | Concept paper: problem, vision, goals, architecture, risks |
| [docs/02-mvp.md](docs/02-mvp.md) | MVP scope, permissions, ride lifecycle, acceptance criteria |
| [docs/03-user-stories.md](docs/03-user-stories.md) | User stories with acceptance criteria, linked to features |
| [docs/04-decisions.md](docs/04-decisions.md) | Architecture decision records and open questions |

## Roadmap

| Version | Theme |
|---|---|
| 1 | MVP: auth, driver approval, vehicles, fare estimate, ride lifecycle, simulated payment, tests, Docker, deployment |
| 2 | Real-time and polish: SignalR, one-at-a-time offers, ratings, earnings, sandbox payments |
| 3 | Growth: routing, real payments, scheduling, promo codes, React and React Native clients |

## Planned stack

C#, ASP.NET Core controllers, EF Core with Npgsql, PostgreSQL, JWT, xUnit, Docker and docker-compose, GitHub Actions.

## Getting started

Setup instructions will be added in feature 1.047 (final README).

## Author

Fred Wombou
