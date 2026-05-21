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
#   - Commands execute directly via `just <target>`
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
    # TODO: add local .devcontainer or update path
    docker build --network=host -t {{IMAGE}} -f "{{ROOT}}/.devcontainer/Containerfile" "{{ROOT}}/.devcontainer"

# Run just target in container (or directly if already in devcontainer).
# Rootless docker: -u 0:0 maps to host user via subuid; writes to the
# bind-mount land owned by the host user. Rootful: direct uid match.
# See feedback_docker_rootless.
[private]
_container +ARGS: _build-image
    #!/usr/bin/env bash
    if [ "${DEVCONTAINER:-}" = "true" ]; then
        just {{ARGS}}
    else
        if docker info --format '{{{{.SecurityOptions}}}}' 2>/dev/null | grep -q rootless; then
            USER_FLAG="-u 0:0"
        else
            USER_FLAG="-u $(id -u):$(id -g)"
        fi
        docker run --rm --network=host \
            $USER_FLAG \
            -v "{{ROOT}}:/workspace:Z" \
            -v "{{ROOT}}/justfile.container:/workspace/justfile:ro" \
            -w /workspace \
            -e DEVCONTAINER=true \
            {{IMAGE}} just {{ARGS}}
    fi

# Run a mutation-testing target with the workspace mounted READ-ONLY.
#
# WHY:
#   Stryker.NET writes mutated sandbox copies into StrykerOutput/. If the
#   workspace is bind-mounted RW (as `_container` does) and the container
#   dies mid-run, the mutated files are left on the host. This helper closes
#   that hole: source is mounted at /src:ro, an in-container tar copy lands
#   in /work (the container's WRITABLE OVERLAY LAYER), and `--rm` destroys
#   the overlay (and the mutated copies) on every exit.
#
# WHAT TOUCHES THE HOST:
#   - {{ROOT}}/.mutants-cache/{nuget,dotnet-tools} — NuGet package cache and
#     dotnet-tool restore output only. NEVER contains mutated source files.
#     Gitignored. Delete the dir to purge the cache.
#   - {{ROOT}}/StrykerOutput/ — only the latest run's HTML/JSON reports are
#     copied out at the end. The mutated sandbox subdirectories are NEVER
#     copied (they die with the container).
#
# WHAT NEVER TOUCHES THE HOST:
#   - Mutated source trees (live in /work, container overlay, --rm wipes).
#   - Stryker's per-mutation sandbox dirs inside StrykerOutput/.
[private]
_container-ephemeral +ARGS: _build-image
    #!/usr/bin/env bash
    set -euo pipefail
    if [ "${DEVCONTAINER:-}" = "true" ]; then
        # Already inside a devcontainer — that container IS the ephemeral
        # boundary. Run directly; the outer just wrapper ensures --rm.
        just {{ARGS}}
        exit 0
    fi
    mkdir -p "{{ROOT}}/StrykerOutput" \
             "{{ROOT}}/.mutants-cache/nuget" \
             "{{ROOT}}/.mutants-cache/dotnet-tools"
    docker run --rm --network=host \
        -v "{{ROOT}}:/src:ro,Z" \
        -v "{{ROOT}}/StrykerOutput:/out:Z" \
        -v "{{ROOT}}/.mutants-cache/nuget:/nuget-cache:Z" \
        -v "{{ROOT}}/.mutants-cache/dotnet-tools:/dotnet-tools:Z" \
        -v "{{ROOT}}/justfile.container:/etc/angzarr-justfile:ro" \
        -w /work \
        -e NUGET_PACKAGES=/nuget-cache \
        -e DOTNET_CLI_HOME=/dotnet-tools \
        -e DOTNET_TOOLS_PATH=/dotnet-tools \
        -e MUTANTS_EPHEMERAL=1 \
        {{IMAGE}} bash -eu -o pipefail -c '
            echo "[ephemeral] copying /src -> /work (container overlay)"
            mkdir -p /work
            # tar|tar: rsync is not guaranteed in the base image. Excludes
            # mirror what rsync would skip — build artifacts, prior mutation
            # output, host-side caches, and stale generated proto trees.
            tar -C /src \
                --exclude=./bin \
                --exclude=./obj \
                --exclude=./.mutants-cache \
                --exclude=./StrykerOutput \
                -cf - . \
                | tar -C /work -xf -
            # Mount the container-side justfile into the copy so `just` finds
            # it (the original /src is read-only, but /work is writable).
            cp /etc/angzarr-justfile /work/justfile
            cd /work
            just {{ARGS}}
            # Persist ONLY the reports back to host. Mutated sandbox dirs
            # (StrykerOutput/<run>/sandbox-*/, etc.) die with the container.
            if [ -d /work/StrykerOutput ]; then
                echo "[ephemeral] copying Stryker reports (no sandboxes) -> /out"
                # Latest run is the most-recently-modified subdir under StrykerOutput.
                LATEST=$(ls -1dt /work/StrykerOutput/*/ 2>/dev/null | head -n1 || true)
                if [ -n "$LATEST" ]; then
                    RUN_NAME=$(basename "$LATEST")
                    mkdir -p "/out/$RUN_NAME"
                    # Copy only the reports/ subtree (HTML + JSON) — never
                    # the sandbox-*/ siblings that hold mutated source.
                    if [ -d "$LATEST/reports" ]; then
                        cp -r "$LATEST/reports" "/out/$RUN_NAME/reports"
                    fi
                    # Top-level mutation-report.json (if present at run root).
                    find "$LATEST" -maxdepth 1 -name "*.json" -exec cp {} "/out/$RUN_NAME/" \;
                    echo "[ephemeral] reports copied to host StrykerOutput/$RUN_NAME/"
                fi
            fi
        '

default:
    @just --list

# =============================================================================
# Proto generation — cross-language model (project_proto_generation_model)
# =============================================================================
# `.proto` sources live in the angzarr-project submodule. Generated C#
# bindings land in Angzarr.Proto/Generated/ and are NEVER committed (see
# .gitignore). They are regenerated:
#   1. on `post-checkout` / `post-merge` via lefthook (covers fresh clones,
#      branch switches, submodule bumps)
#   2. transparently as a recipe dependency of `build`, `test`, `fmt`, etc.
#      The recipe is idempotent — mtime guard skips when bindings are newer
#      than the newest .proto source.
#
# Runs in the same devcontainer image used for build/test/mutation so the
# buf + protoc-gen-csharp + protoc-gen-grpc-csharp toolchain is fixed (no
# host fallback). Rootless docker → `-u 0:0` per feedback_docker_rootless.
#
# `buf generate` is the EXECUTOR; this `just` recipe is the TRIGGER. Plain
# `dotnet build` consumes the pre-emitted Angzarr.Proto/Generated/*.cs files
# via the .NET SDK's default Compile glob — no build-tool integration runs
# protoc transitively. Matches the 6-lang ecosystem pattern.

PROTO_SRC_DIR := ROOT + "/angzarr-project/proto"
PROTO_OUT_DIR := ROOT + "/Angzarr.Proto/Generated"

# Public entry point. Idempotent: returns immediately if bindings are
# fresher than the newest .proto source.
generate-proto:
    #!/usr/bin/env bash
    set -euo pipefail
    src_dir="{{PROTO_SRC_DIR}}"
    out_dir="{{PROTO_OUT_DIR}}"
    if [ ! -d "$src_dir" ]; then
        echo "[generate-proto] $src_dir missing — is the angzarr-project submodule initialized?" >&2
        exit 1
    fi
    # Staleness check: regenerate if any .proto file is newer than the
    # OLDEST generated binding, or if no bindings exist yet.
    # Catches "submodule bumped" and "fresh clone" — the hot paths driving
    # the lefthook trigger. Does NOT catch manual deletion of one binding
    # while others remain fresh; use `just generate-proto-force` for that.
    #
    # OLDEST (matches Python/Java) — `buf generate` writes a fresh tree on
    # every invocation, so no orphan-stale leftovers exist. (Go's NEWEST
    # adaptation unnecessary here.)
    newest_proto=$(find "$src_dir" -name '*.proto' -printf '%T@\n' 2>/dev/null \
                    | sort -n | tail -1)
    # Guard the find for out_dir — on clean state Angzarr.Proto/Generated
    # does not yet exist, and `find $missing` exits non-zero which trips
    # pipefail.
    if [ -d "$out_dir" ]; then
        oldest_pb=$(find "$out_dir" -name '*.cs' -printf '%T@\n' 2>/dev/null \
                        | sort -n | head -1)
    else
        oldest_pb=""
    fi
    if [ -n "$newest_proto" ] && [ -n "$oldest_pb" ] \
        && awk -v p="$newest_proto" -v b="$oldest_pb" 'BEGIN{exit !(b>p)}'; then
        echo "[generate-proto] bindings up-to-date, skipping (use 'just generate-proto-force' to override)"
        exit 0
    fi
    just generate-proto-force

# Always regenerate, ignoring mtimes. Invoked by `generate-proto` when stale
# and exposed directly for users who want to force a rebuild.
generate-proto-force: _build-image
    #!/usr/bin/env bash
    set -euo pipefail
    if [ "${DEVCONTAINER:-}" = "true" ]; then
        # Inside the devcontainer image already — run directly.
        just --justfile "{{ROOT}}/justfile.container" generate-proto-force
        exit 0
    fi
    # Rootless docker: -u 0:0 maps to host user via subuid; writes to the
    # bind-mount land owned by the host user. Rootful: direct uid match.
    # See feedback_docker_rootless.
    if docker info --format '{{{{.SecurityOptions}}}}' 2>/dev/null | grep -q rootless; then
        USER_FLAG="-u 0:0"
    else
        USER_FLAG="-u $(id -u):$(id -g)"
    fi
    docker run --rm --network=host \
        $USER_FLAG \
        -v "{{ROOT}}:/workspace:Z" \
        -v "{{ROOT}}/justfile.container:/workspace/justfile:ro" \
        -w /workspace \
        -e DEVCONTAINER=true \
        {{IMAGE}} just generate-proto-force

# Legacy alias — kept so existing recipe-deps and muscle memory keep working.
proto: generate-proto
proto-gen: generate-proto

restore: generate-proto
    just _container restore

build: generate-proto
    just _container build

build-dev: generate-proto
    just _container build-dev

test-unit: generate-proto
    just _container test-unit

test-acceptance: generate-proto
    just _container test-acceptance

test: generate-proto
    just _container test

fmt: generate-proto
    just _container fmt

lint: generate-proto
    just _container lint

# Cross-language alias — `just check` runs lint + fmt-check.
check: fmt lint

# Run Stryker.NET mutation tests inside an ephemeral container.
# Source is mounted READ-ONLY; mutated copies live in the container overlay
# and die with --rm. Only HTML/JSON reports are persisted to StrykerOutput/.
# Host dotnet/stryker invocations are FORBIDDEN — always go through `just`.
mutation-test: generate-proto
    just _container-ephemeral mutation-test

# Mutation image: re-uses the example devcontainer build (the
# .devcontainer/Containerfile now installs dotnet-stryker 4.14.1 per
# .plan/mutation-container-isolation.md), so we just alias to _build-image.
[private]
_ensure-image: _build-image

# Cross-language `mutate` recipe per .plan/mutation-container-isolation.md.
mutate: generate-proto _ensure-image
    mkdir -p "{{ROOT}}/mutants-reports"
    docker run --rm --network=host \
        -u 0:0 \
        --mount type=bind,src="{{ROOT}}",dst=/src,readonly \
        --tmpfs /work:rw,exec,size=4g \
        --mount type=bind,src="{{ROOT}}/mutants-reports",dst=/reports \
        -w /work \
        {{IMAGE}} \
        bash -eu -o pipefail -c '\
            if command -v rsync >/dev/null 2>&1; then \
                rsync -a /src/ /work/; \
            else \
                tar -C /src -cf - . | tar -C /work -xf -; \
            fi && \
            cd /work && \
            (dotnet stryker --reporter json --reporter html 2>&1 || true) && \
            (LATEST=$(ls -1dt /work/StrykerOutput/*/ 2>/dev/null | head -n1 || true); \
             if [ -n "$LATEST" ]; then \
                 RUN=$(basename "$LATEST"); \
                 mkdir -p "/reports/$RUN"; \
                 [ -d "$LATEST/reports" ] && cp -r "$LATEST/reports" "/reports/$RUN/reports"; \
             fi) && \
            echo "[mutate] stryker reports copied to host mutants-reports/" \
        '

mutants: mutate

# Purge the local mutation cache (.mutants-cache/) — NuGet packages and
# dotnet-tool restore output only; never holds mutated source.
mutation-purge-cache:
    rm -rf "{{ROOT}}/.mutants-cache"
    @echo "Removed {{ROOT}}/.mutants-cache"

# Run poker in standalone mode (host - needs Rust)
run: build
    mkdir -p "{{ROOT}}/data"
    cd "{{ROOT}}" && cargo run \
        --bin angzarr-standalone \
        --features standalone,sqlite \
        -- --config standalone.yaml

clean:
    just _container "dotnet clean /workspace/Angzarr.Examples.sln" || true
    rm -rf "{{ROOT}}/data" "{{ROOT}}/Angzarr.Proto/Generated"
    find "{{ROOT}}" -type d \( -name 'bin' -o -name 'obj' \) -not -path "*/angzarr-client-csharp/*" -exec rm -rf {} + 2>/dev/null || true

# Auto-format code
fmt-fix:
    just _container fmt-fix
