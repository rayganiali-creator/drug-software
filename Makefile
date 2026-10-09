# Developer shortcuts. Requires: docker (compose v2), .NET 10 SDK, Python 3.11+, Node 22, Flutter (stable).
SHELL := /bin/bash
DOTNET_ENV := set -a; . ./.env; set +a;

.PHONY: help env up down infra-up migrate api db-up api-memory test-pg ai-install ai-run web-install web-run test test-integration lint

help:
	@grep -E '^[a-z-]+:' Makefile | cut -d: -f1 | tr '\n' ' '; echo

env: ## create .env with freshly generated local-only passwords
	@./scripts/init-env.sh

infra-up: ## start postgres, redis, opensearch, kafka
	docker compose up -d --wait postgres redis opensearch kafka

up: ## start everything (infra + api + ai + web containers)
	docker compose --profile app up -d --build --wait

down:
	docker compose --profile app down

migrate: ## apply EF Core migrations to the local database
	$(DOTNET_ENV) dotnet run --project src/Host/MedSmarter.Api --no-launch-profile -- --migrate-and-exit

api: ## run the API on the host (http://localhost:5080)
	$(DOTNET_ENV) ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5080 dotnet run --project src/Host/MedSmarter.Api --no-launch-profile

ai-install:
	cd ai && python3 -m venv .venv && .venv/bin/pip install -e ".[dev]"

ai-run:
	$(DOTNET_ENV) cd ai && .venv/bin/uvicorn app.main:app --port 8000

web-install:
	cd web && npm ci

web-run:
	cd web && npm run dev

web-run-api: ## web app signed in through the running API (needed for the drug reference)
	cd web && VITE_AUTH_MODE=api VITE_API_BASE_URL=http://localhost:5080 npm run dev

db-up: ## only PostgreSQL in Docker (enough for the API with Persistence__Provider=Postgres)
	docker compose up -d --wait postgres

api-memory: ## run the API with the in-memory store (no database; data is lost on restart; Development only)
	$(DOTNET_ENV) ASPNETCORE_ENVIRONMENT=Development ASPNETCORE_URLS=http://127.0.0.1:5080 Persistence__Provider=InMemory Redis__ConnectionString=localhost:6379 OpenSearch__Uri=http://localhost:9200 Kafka__BootstrapServers=localhost:29092 Cors__AllowedOrigins__0=http://localhost:5173 dotnet run --project src/Host/MedSmarter.Api --no-launch-profile

test-pg: ## Phase 5 + persistence tests against a real local PostgreSQL (set MEDSMARTER_PG_TEST to a connection string whose user may create databases)
	MEDSMARTER_PG_TEST="$${MEDSMARTER_PG_TEST:?set MEDSMARTER_PG_TEST}" dotnet test tests/MedSmarter.Patients.Tests
	MEDSMARTER_PG_TEST="$${MEDSMARTER_PG_TEST:?set MEDSMARTER_PG_TEST}" dotnet test tests/MedSmarter.Knowledge.Tests --filter PersistenceTests

test: ## unit tests for all four codebases (no infrastructure needed)
	dotnet test MedSmarter.sln
	cd ai && .venv/bin/pytest
	cd web && npm test
	cd mobile && flutter test

test-integration: ## needs `make infra-up migrate`
	$(DOTNET_ENV) MEDSMARTER_IT=1 dotnet test tests/MedSmarter.IntegrationTests

lint:
	dotnet build MedSmarter.sln -warnaserror
	cd ai && .venv/bin/ruff check . && .venv/bin/ruff format --check .
	cd web && npm run lint && npm run typecheck
	cd mobile && flutter analyze
