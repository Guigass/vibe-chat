#!/usr/bin/env bash
# Idempotent bootstrap for Cursor Cloud Agents / local contributors.
# Installs tooling when missing, brings up Compose deps, migrates and seeds when possible.
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
cd "$ROOT_DIR"

export PATH="${HOME}/.dotnet:${HOME}/.dotnet/tools:${HOME}/.local/bin:${PATH}"
export DOTNET_ROOT="${DOTNET_ROOT:-${HOME}/.dotnet}"
export DOTNET_NOLOGO=1
export DOTNET_CLI_TELEMETRY_OPTOUT=1

log() { printf '==> %s\n' "$*"; }
warn() { printf 'warn: %s\n' "$*" >&2; }

ensure_dotnet() {
  if command -v dotnet >/dev/null 2>&1; then
    log "dotnet present: $(dotnet --version)"
    return 0
  fi
  log "Installing .NET SDK 10..."
  curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
  bash /tmp/dotnet-install.sh --channel 10.0 --install-dir "${HOME}/.dotnet"
  export PATH="${HOME}/.dotnet:${HOME}/.dotnet/tools:${PATH}"
  export DOTNET_ROOT="${HOME}/.dotnet"
  log "dotnet installed: $(dotnet --version)"
}

ensure_node() {
  if command -v node >/dev/null 2>&1; then
    log "node present: $(node --version)"
    return 0
  fi
  if [[ -x "${HOME}/.nvm/versions/node/v22.22.3/bin/node" ]]; then
    export PATH="${HOME}/.nvm/versions/node/v22.22.3/bin:${PATH}"
    log "node via nvm: $(node --version)"
    return 0
  fi
  warn "Node.js not found. Install Node 22+ for Angular. Continuing without web deps."
  return 1
}

ensure_task() {
  if command -v task >/dev/null 2>&1; then
    log "task present: $(task --version)"
    return 0
  fi
  log "Installing go-task..."
  mkdir -p "${HOME}/.local/bin"
  sh -c "$(curl -fsSL https://taskfile.dev/install.sh)" -- -d -b "${HOME}/.local/bin"
  export PATH="${HOME}/.local/bin:${PATH}"
  log "task installed: $(task --version)"
}

ensure_dotnet_ef() {
  if dotnet ef --version >/dev/null 2>&1; then
    return 0
  fi
  log "Installing dotnet-ef tool..."
  dotnet tool update -g dotnet-ef >/dev/null 2>&1 || dotnet tool install -g dotnet-ef
}

# Cursor Cloud Agent VMs ship without Docker and without systemd, so Testcontainers
# suites fail unless the daemon is installed and started by hand. Only touches hosts
# with apt-get + passwordless sudo; a contributor laptop falls through untouched.
ensure_docker() {
  if docker info >/dev/null 2>&1; then
    log "docker ready: $(docker --version)"
    return 0
  fi

  if ! sudo -n true >/dev/null 2>&1; then
    warn "Docker unavailable and no passwordless sudo; skipping compose and Testcontainers"
    return 1
  fi

  if ! command -v docker >/dev/null 2>&1; then
    if ! command -v apt-get >/dev/null 2>&1; then
      warn "Docker not installed and apt-get unavailable; install Docker manually"
      return 1
    fi
    log "Installing Docker (docker.io + compose v2 + fuse-overlayfs)..."
    sudo -n env DEBIAN_FRONTEND=noninteractive apt-get update -qq
    sudo -n env DEBIAN_FRONTEND=noninteractive apt-get install -y -qq \
      docker.io docker-compose-v2 fuse-overlayfs
  fi

  # Docker 29 defaults to the containerd snapshotter, which cannot run on this VM.
  if [[ ! -f /etc/docker/daemon.json ]]; then
    log "Writing /etc/docker/daemon.json (fuse-overlayfs; containerd snapshotter off)"
    sudo -n mkdir -p /etc/docker
    printf '%s\n' '{
  "storage-driver": "fuse-overlayfs",
  "features": { "containerd-snapshotter": false }
}' | sudo -n tee /etc/docker/daemon.json >/dev/null
  fi

  if ! pgrep -x dockerd >/dev/null 2>&1; then
    log "Starting dockerd (log: /tmp/dockerd.log)..."
    sudo -n sh -c 'nohup dockerd >/tmp/dockerd.log 2>&1 &'
  fi

  # Some Cloud Agent images leave /var/run as 0700 (not a symlink to /run), so
  # chmod 666 on the socket alone is not enough for the unprivileged agent user.
  sudo -n chmod 755 /var/run 2>/dev/null || true

  local attempt
  for attempt in $(seq 1 30); do
    if [[ -S /var/run/docker.sock ]]; then
      sudo -n chmod 666 /var/run/docker.sock || true
    fi
    if docker info >/dev/null 2>&1; then
      log "docker ready after ${attempt} attempt(s): $(docker --version)"
      return 0
    fi
    sleep 2
  done

  warn "dockerd did not become ready; see /tmp/dockerd.log"
  return 1
}

ensure_env_file() {
  if [[ ! -f .env ]]; then
    log "Copying .env.example → .env"
    cp .env.example .env
  else
    log ".env already present"
  fi
}

restore_and_npm() {
  log "Restoring .NET solution..."
  dotnet restore VibeChat.slnx

  if ensure_node; then
    if [[ -f apps/web/package-lock.json ]]; then
      log "Installing web dependencies..."
      npm ci --prefix apps/web
    else
      npm install --prefix apps/web
    fi
    if [[ -f tests/e2e/package.json ]]; then
      log "Installing e2e dependencies..."
      npm ci --prefix tests/e2e 2>/dev/null || npm install --prefix tests/e2e
      npx --prefix tests/e2e playwright install chromium || warn "Playwright browser install skipped"
    fi
  fi
}

start_compose() {
  if ! docker info >/dev/null 2>&1; then
    warn "Docker daemon not reachable; skipping compose up"
    return 0
  fi
  if ! docker compose version >/dev/null 2>&1; then
    warn "Compose v2 plugin missing; skipping compose up"
    return 0
  fi
  log "Starting Compose data plane..."
  docker compose up -d postgres redis keycloak minio createbucket
  ./infra/scripts/wait-for-healthy.sh postgres redis keycloak minio || warn "Health wait timed out"
}

migrate_and_seed() {
  ensure_dotnet_ef || true
  if command -v docker >/dev/null 2>&1 && docker compose ps postgres 2>/dev/null | grep -q healthy; then
    log "Applying EF migrations..."
    set -a
    # shellcheck disable=SC1091
    [[ -f .env ]] && source .env
    set +a
    export ConnectionStrings__Database="${DATABASE_URL:-Host=localhost;Port=5432;Database=vibechat;Username=vibechat;Password=vibechat_dev_password_change_me}"
    if dotnet ef database update \
      --project src/VibeChat.Infrastructure/VibeChat.Infrastructure.csproj \
      --startup-project apps/api/VibeChat.Api.csproj; then
      log "Migrations applied"
    else
      warn "EF migrate failed (API may migrate on startup when Seed:Enabled=true)"
    fi
  else
    warn "Postgres not healthy; skip migrate (will run on API startup if Seed:Enabled)"
  fi

  # Seed requires a running API. Soft-fail during agent bootstrap.
  if curl -fsS "${SEED_API_URL:-http://localhost:5080/api/v1/dev/seed}" -X POST \
    -H 'Content-Type: application/json' -d '{}' >/tmp/vibechat-agent-seed.json 2>/dev/null; then
    log "Seed succeeded via API"
  else
    warn "Seed skipped (API not running yet). Use: task seed after task dev"
  fi
}

main() {
  log "VibeChat agent setup (idempotent)"
  ensure_dotnet
  ensure_task
  ensure_env_file
  ensure_docker || true
  restore_and_npm
  start_compose
  migrate_and_seed
  log "Agent setup complete. Next: task dev"
}

main "$@"
