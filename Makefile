# GAM.NET Makefile
# Simplifies container management and testing with different BM25 backends

.PHONY: help up down status logs test clean
.PHONY: up-default up-timescale up-vectorchord
.PHONY: down-default down-timescale down-vectorchord
.PHONY: use-default use-timescale use-vectorchord
.PHONY: test-default test-timescale test-vectorchord
.PHONY: build run

# Default target
help:
	@echo "GAM.NET Development Commands"
	@echo "============================"
	@echo ""
	@echo "Container Management:"
	@echo "  make up                 Start default postgres (pgvector only, port 5432)"
	@echo "  make up-timescale       Start Timescale with pg_textsearch (port 5433)"
	@echo "  make up-vectorchord     Start VectorChord with vchord_bm25 (port 5434)"
	@echo "  make up-all             Start all postgres containers"
	@echo "  make down               Stop default postgres"
	@echo "  make down-timescale     Stop Timescale container"
	@echo "  make down-vectorchord   Stop VectorChord container"
	@echo "  make down-all           Stop all postgres containers"
	@echo "  make status             Show running containers"
	@echo "  make logs               Show logs for default postgres"
	@echo "  make clean              Remove all containers and volumes"
	@echo ""
	@echo "Configuration (updates appsettings.json):"
	@echo "  make use-default        Configure samples to use default postgres (5432)"
	@echo "  make use-timescale      Configure samples to use Timescale (5433)"
	@echo "  make use-vectorchord    Configure samples to use VectorChord (5434)"
	@echo ""
	@echo "Build & Test:"
	@echo "  make build              Build all projects"
	@echo "  make test               Run unit tests"
	@echo "  make test-integration   Run integration tests"
	@echo "  make run                Run console sample"
	@echo "  make run-query          Run console sample in query mode"
	@echo "  make run-test           Run console sample comprehensive tests"
	@echo ""
	@echo "Quick Start Examples:"
	@echo "  make up-timescale use-timescale run-test"
	@echo "  make up-vectorchord use-vectorchord run-test"

# ============================================================================
# Container Management
# ============================================================================

up: up-default
down: down-default

up-default:
	@echo "Starting default postgres (pgvector only)..."
	docker compose up -d
	@echo "Postgres available at localhost:5432"

up-timescale:
	@echo "Starting Timescale with pg_textsearch..."
	docker compose -f samples/docker-compose.timescale.yml up -d
	@echo "Waiting for container to be healthy..."
	@sleep 5
	@docker exec gam-postgres-timescale psql -U postgres -d gam -c "SELECT 'pg_textsearch ready' WHERE EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_textsearch');" 2>/dev/null || echo "Still initializing..."
	@echo "Timescale available at localhost:5433"

up-vectorchord:
	@echo "Starting VectorChord with vchord_bm25..."
	docker compose -f samples/docker-compose.vectorchord.yml up -d
	@echo "Waiting for container to be healthy..."
	@sleep 5
	@docker exec gam-postgres-vectorchord psql -U postgres -d gam -c "SELECT 'vchord_bm25 ready' WHERE EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'vchord_bm25');" 2>/dev/null || echo "Still initializing..."
	@echo "VectorChord available at localhost:5434"

up-all: up-default up-timescale up-vectorchord
	@echo ""
	@echo "All containers started:"
	@make status

down-default:
	docker compose down

down-timescale:
	docker compose -f samples/docker-compose.timescale.yml down

down-vectorchord:
	docker compose -f samples/docker-compose.vectorchord.yml down

down-all: down-default down-timescale down-vectorchord

status:
	@echo "GAM.NET Postgres Containers:"
	@echo "============================"
	@docker ps --format "table {{.Names}}\t{{.Image}}\t{{.Status}}\t{{.Ports}}" | grep -E "(NAMES|gam-postgres)" || echo "No containers running"
	@echo ""
	@echo "Detected BM25 backends:"
	@docker exec gam-postgres psql -U postgres -d gam -c "SELECT 'default (5432): native_fts' as backend;" 2>/dev/null || true
	@docker exec gam-postgres-timescale psql -U postgres -d gam -c "SELECT 'timescale (5433): pg_textsearch' as backend WHERE EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_textsearch');" 2>/dev/null || true
	@docker exec gam-postgres-vectorchord psql -U postgres -d gam -c "SELECT 'vectorchord (5434): vchord_bm25' as backend WHERE EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'vchord_bm25');" 2>/dev/null || true

logs:
	docker compose logs -f postgres

logs-timescale:
	docker compose -f samples/docker-compose.timescale.yml logs -f postgres

logs-vectorchord:
	docker compose -f samples/docker-compose.vectorchord.yml logs -f postgres

clean:
	@echo "Stopping and removing all GAM containers and volumes..."
	docker compose down -v 2>/dev/null || true
	docker compose -f samples/docker-compose.timescale.yml down -v 2>/dev/null || true
	docker compose -f samples/docker-compose.vectorchord.yml down -v 2>/dev/null || true
	@echo "Done."

# ============================================================================
# Configuration - Update appsettings.json
# ============================================================================

CONSOLE_SETTINGS = samples/Gam.Sample.Console/appsettings.json
WEBAPI_SETTINGS = samples/Gam.Sample.WebApi/appsettings.json

use-default:
	@echo "Configuring samples to use default postgres (port 5432)..."
	@sed -i.bak 's|Host=localhost;Port=[0-9]*;|Host=localhost;|g' $(CONSOLE_SETTINGS) $(WEBAPI_SETTINGS) 2>/dev/null || \
		sed -i '' 's|Host=localhost;Port=[0-9]*;|Host=localhost;|g' $(CONSOLE_SETTINGS) $(WEBAPI_SETTINGS)
	@sed -i.bak 's|Host=localhost;Database|Host=localhost;Port=5432;Database|g' $(CONSOLE_SETTINGS) $(WEBAPI_SETTINGS) 2>/dev/null || \
		sed -i '' 's|Host=localhost;Database|Host=localhost;Port=5432;Database|g' $(CONSOLE_SETTINGS) $(WEBAPI_SETTINGS)
	@rm -f $(CONSOLE_SETTINGS).bak $(WEBAPI_SETTINGS).bak 2>/dev/null || true
	@echo "Connection string: Host=localhost;Port=5432;Database=gam;..."
	@echo "BM25 backend: native_fts (fallback)"

use-timescale:
	@echo "Configuring samples to use Timescale postgres (port 5433)..."
	@sed -i.bak 's|Host=localhost;Port=[0-9]*;|Host=localhost;Port=5433;|g' $(CONSOLE_SETTINGS) $(WEBAPI_SETTINGS) 2>/dev/null || \
		sed -i '' 's|Host=localhost;Port=[0-9]*;|Host=localhost;Port=5433;|g' $(CONSOLE_SETTINGS) $(WEBAPI_SETTINGS)
	@sed -i.bak 's|Host=localhost;Database|Host=localhost;Port=5433;Database|g' $(CONSOLE_SETTINGS) $(WEBAPI_SETTINGS) 2>/dev/null || \
		sed -i '' 's|Host=localhost;Database|Host=localhost;Port=5433;Database|g' $(CONSOLE_SETTINGS) $(WEBAPI_SETTINGS)
	@rm -f $(CONSOLE_SETTINGS).bak $(WEBAPI_SETTINGS).bak 2>/dev/null || true
	@echo "Connection string: Host=localhost;Port=5433;Database=gam;..."
	@echo "BM25 backend: pg_textsearch"

use-vectorchord:
	@echo "Configuring samples to use VectorChord postgres (port 5434)..."
	@sed -i.bak 's|Host=localhost;Port=[0-9]*;|Host=localhost;Port=5434;|g' $(CONSOLE_SETTINGS) $(WEBAPI_SETTINGS) 2>/dev/null || \
		sed -i '' 's|Host=localhost;Port=[0-9]*;|Host=localhost;Port=5434;|g' $(CONSOLE_SETTINGS) $(WEBAPI_SETTINGS)
	@sed -i.bak 's|Host=localhost;Database|Host=localhost;Port=5434;Database|g' $(CONSOLE_SETTINGS) $(WEBAPI_SETTINGS) 2>/dev/null || \
		sed -i '' 's|Host=localhost;Database|Host=localhost;Port=5434;Database|g' $(CONSOLE_SETTINGS) $(WEBAPI_SETTINGS)
	@rm -f $(CONSOLE_SETTINGS).bak $(WEBAPI_SETTINGS).bak 2>/dev/null || true
	@echo "Connection string: Host=localhost;Port=5434;Database=gam;..."
	@echo "BM25 backend: vchord_bm25"

# ============================================================================
# Build & Test
# ============================================================================

build:
	dotnet build Gam.sln

test:
	dotnet test tests/Gam.Core.Tests/Gam.Core.Tests.csproj

test-integration:
	dotnet test tests/Gam.Integration.Tests/Gam.Integration.Tests.csproj

run:
	dotnet run --project samples/Gam.Sample.Console

run-query:
	dotnet run --project samples/Gam.Sample.Console -- --query

run-test:
	dotnet run --project samples/Gam.Sample.Console -- --test

run-webapi:
	dotnet run --project samples/Gam.Sample.WebApi

# ============================================================================
# Quick test commands - start container, configure, and run tests
# ============================================================================

test-default: up-default use-default
	@echo ""
	@echo "Testing with default postgres (native FTS)..."
	@make run-test

test-timescale: up-timescale use-timescale
	@echo ""
	@echo "Testing with Timescale (pg_textsearch)..."
	@make run-test

test-vectorchord: up-vectorchord use-vectorchord
	@echo ""
	@echo "Testing with VectorChord (vchord_bm25)..."
	@make run-test
