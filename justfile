# C# poker examples
#
# Container Overlay Pattern:
# --------------------------
# This justfile uses an overlay pattern for container execution:
#
# 1. `justfile` (this file) - runs on the host, delegates to container
# 2. `justfile.container` - mounted over this file inside the container
#
# When running outside a devcontainer:
#   - Builds/uses local devcontainer image with `just` pre-installed
#   - Podman mounts justfile.container as /workspace/justfile
#   - `just build` on host → docker runs → `just build` in container → dotnet
#
# When running inside a devcontainer (DEVCONTAINER=true):
#   - Commands execute directly via `just --justfile justfile.container <target>`
#   - No container nesting

set shell := ["bash", "-c"]

# Reusable submodule-protection recipes (install-submodule-hooks,
# check-submodules-clean). Source of truth: angzarr-project/submodule.just.
import? 'angzarr-project/submodule.just'

ROOT := `git rev-parse --show-toplevel`
IMAGE := "angzarr-csharp-dev"

# Build the devcontainer image
[private]
_build-image:
    #!/usr/bin/env bash
    set -euo pipefail
    # Inside a devcontainer _container runs recipes directly, so no image is
    # needed (and there is no docker to build one with).
    if [ "${DEVCONTAINER:-}" = "true" ]; then
        exit 0
    fi
    # TODO: add local .devcontainer or update path
    docker build --network=host -t {{IMAGE}} -f "{{ROOT}}/.devcontainer/Containerfile" "{{ROOT}}/.devcontainer"

# Run just target in container (or directly if already in devcontainer)
[private]
_container +ARGS: _build-image
    #!/usr/bin/env bash
    if [ "${DEVCONTAINER:-}" = "true" ]; then
        # Name the container justfile explicitly. A bare `just {{ARGS}}`
        # resolves to THIS (host) justfile, whose recipes call `_container`
        # again: build -> _container build -> just build -> ... with no limit.
        just --justfile "{{ROOT}}/justfile.container" {{ARGS}}
    else
        # Mount the shared git dir at its host path so a linked worktree
        # (whose .git file points there) resolves inside the container;
        # safe.directory lets git read a checkout owned by another uid.
        git_common="$(git rev-parse --path-format=absolute --git-common-dir)"
        # Rootless docker maps container uid 0 to the host user, so `-u 0:0`
        # makes writes to the mounted checkout land as the host user; the
        # image's default user maps to an unowned subuid there and cannot
        # write it. Rootful docker keeps the image default.
        user_flag=()
        if [[ "$(docker info --format '{{{{json .SecurityOptions}}')" == *rootless* ]]; then
            user_flag=(-u 0:0)
        fi
        docker run --rm --network=host "${user_flag[@]}" \
            -v "{{ROOT}}:/workspace:Z" \
            -v "${git_common}:${git_common}" \
            -e GIT_CONFIG_COUNT=1 \
            -e GIT_CONFIG_KEY_0=safe.directory \
            -e GIT_CONFIG_VALUE_0='*' \
            -v "{{ROOT}}/justfile.container:/workspace/justfile:ro" \
            -w /workspace \
            {{IMAGE}} just {{ARGS}}
    fi

default:
    @just --list

restore:
    just _container restore

build:
    just _container build

build-dev:
    just _container build-dev

test-unit:
    just _container test-unit

test-acceptance:
    just _container test-acceptance

test:
    just _container test

fmt:
    just _container fmt

lint:
    just _container lint

# Run poker in standalone mode (host - needs Rust)
run: build
    mkdir -p "{{ROOT}}/data"
    cd "{{ROOT}}" && cargo run \
        --bin angzarr-standalone \
        --features standalone,sqlite \
        -- --config standalone.yaml

clean:
    just _container "dotnet clean /workspace/Angzarr.Examples.sln" || true
    rm -rf "{{ROOT}}/data"
    find "{{ROOT}}" -type d \( -name 'bin' -o -name 'obj' \) -not -path "*/angzarr-client-csharp/*" -exec rm -rf {} + 2>/dev/null || true
