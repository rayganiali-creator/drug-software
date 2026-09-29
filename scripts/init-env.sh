#!/usr/bin/env bash
# Creates .env from .env.example with random LOCAL-ONLY passwords. Refuses to overwrite.
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ -e .env ]]; then echo ".env already exists; leaving it untouched." >&2; exit 0; fi
pg=$(openssl rand -hex 16)
rd=$(openssl rand -hex 16)
sed -e "s/^POSTGRES_PASSWORD=.*/POSTGRES_PASSWORD=${pg}/" \
    -e "s/^REDIS_PASSWORD=.*/REDIS_PASSWORD=${rd}/" \
    -e "s/Password=change-me/Password=${pg}/" \
    -e "s/password=change-me/password=${rd}/" .env.example > .env
chmod 600 .env
echo "Created .env (git-ignored)."
