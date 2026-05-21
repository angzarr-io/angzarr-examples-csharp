# syntax=docker/dockerfile:1.4
# C# poker examples - optimized multi-stage build
# Build: podman build -t poker-csharp-player --target agg-player -f Containerfile .
# Context is the examples-csharp repo root (with buf-exported proto sources)
#
# Optimizations:
# 1. Shared restore stage - NuGet restore runs once, packages in image layer
# 2. Slim Debian runtime - minimal attack surface
# 3. Multi-arch support (amd64 + arm64)
#
# Note: Using Debian-based images (not Alpine) for glibc compatibility.

ARG DOTNET_VERSION=8.0

# ============================================================================
# Base builder - .NET SDK (Debian bookworm)
# ============================================================================
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION}-bookworm-slim AS base

RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates git \
    && rm -rf /var/lib/apt/lists/*

# INFRA-1: trust any bind-mounted workspace path (rootless docker friendliness).
RUN git config --system --add safe.directory '*' \
 && git config --system --add safe.directory '/workspace' \
 && git config --system --add safe.directory '/angzarr'

WORKDIR /app

# ============================================================================
# Restore - download all NuGet dependencies once
# ============================================================================
FROM base AS restore

WORKDIR /app

# Copy client library submodule (needed for Angzarr.Client ProjectReference)
COPY angzarr-client-csharp ./angzarr-client-csharp

# Copy pre-generated proto code (buf generate runs before docker build)
COPY Angzarr.Proto/Generated ./Angzarr.Proto/Generated

# Copy MSBuild customization (redirects client's Angzarr.Proto to main)
COPY Directory.Build.targets ./

# Copy solution and project files for dependency resolution
COPY Angzarr.Examples.sln ./
COPY Angzarr.Proto/Angzarr.Proto.csproj ./Angzarr.Proto/
COPY Player/Agg/Player.Agg.csproj ./Player/Agg/
COPY Table/Agg/Table.Agg.csproj ./Table/Agg/
COPY Hand/Agg/Hand.Agg.csproj ./Hand/Agg/
COPY Table/SagaHand/Table.SagaHand.csproj ./Table/SagaHand/
COPY Table/SagaPlayer/Table.SagaPlayer.csproj ./Table/SagaPlayer/
COPY Hand/SagaTable/Hand.SagaTable.csproj ./Hand/SagaTable/
COPY Hand/SagaPlayer/Hand.SagaPlayer.csproj ./Hand/SagaPlayer/
COPY HandFlow/HandFlow.csproj ./HandFlow/
COPY PrjOutput/PrjOutput.csproj ./PrjOutput/
COPY Tests/Tests.csproj ./Tests/
COPY Player/Upc/Player.Upc.csproj ./Player/Upc/
COPY PrjCloudEvents/PrjCloudEvents.csproj ./PrjCloudEvents/
COPY Player/SagaTable/Player.SagaTable.csproj ./Player/SagaTable/
COPY Tournament/Agg/Tournament.Agg.csproj ./Tournament/Agg/

# Restore all packages into the image layer (not just cache mount)
# so they survive GHA layer caching and --no-restore works in build stages
RUN dotnet restore

# ============================================================================
# Source - copy all C# source
# ============================================================================
FROM restore AS source

# Copy all source files
COPY . ./

# Unify Angzarr.Proto: replace client submodule's Grpc.Tools-based proto project
# with the main pre-generated one to avoid duplicate assembly conflicts
RUN cp Angzarr.Proto/Angzarr.Proto.csproj angzarr-client-csharp/Angzarr.Proto/Angzarr.Proto.csproj && \
    cp -r Angzarr.Proto/Generated angzarr-client-csharp/Angzarr.Proto/

# ============================================================================
# Aggregate builds
# ============================================================================
FROM source AS build-player
WORKDIR /app
RUN dotnet publish Player/Agg/Player.Agg.csproj -c Release -o /out --no-restore

FROM source AS build-table
WORKDIR /app
RUN dotnet publish Table/Agg/Table.Agg.csproj -c Release -o /out --no-restore

FROM source AS build-hand
WORKDIR /app
RUN dotnet publish Hand/Agg/Hand.Agg.csproj -c Release -o /out --no-restore

# ============================================================================
# Saga builds
# ============================================================================
FROM source AS build-saga-table-hand
WORKDIR /app
RUN dotnet publish Table/SagaHand/Table.SagaHand.csproj -c Release -o /out --no-restore

FROM source AS build-saga-table-player
WORKDIR /app
RUN dotnet publish Table/SagaPlayer/Table.SagaPlayer.csproj -c Release -o /out --no-restore

FROM source AS build-saga-hand-table
WORKDIR /app
RUN dotnet publish Hand/SagaTable/Hand.SagaTable.csproj -c Release -o /out --no-restore

FROM source AS build-saga-hand-player
WORKDIR /app
RUN dotnet publish Hand/SagaPlayer/Hand.SagaPlayer.csproj -c Release -o /out --no-restore

# ============================================================================
# Process Manager build
# ============================================================================
FROM source AS build-pmg-hand-flow
WORKDIR /app
RUN dotnet publish HandFlow/HandFlow.csproj -c Release -o /out --no-restore

# ============================================================================
# Projector build
# ============================================================================
FROM source AS build-prj-output
WORKDIR /app
RUN dotnet publish PrjOutput/PrjOutput.csproj -c Release -o /out --no-restore

# ============================================================================
# Runtime base - ASP.NET Core runtime (required for gRPC)
# ============================================================================
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION}-bookworm-slim AS runtime
WORKDIR /app
RUN adduser --disabled-password --gecos "" --uid 1000 angzarr
USER angzarr

# ============================================================================
# Domain Aggregates
# ============================================================================
FROM runtime AS agg-player
COPY --from=build-player --chown=angzarr:angzarr /out ./
ENV PORT=50501 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
EXPOSE 50501
ENTRYPOINT ["./Player.Agg"]

FROM runtime AS agg-table
COPY --from=build-table --chown=angzarr:angzarr /out ./
ENV PORT=50502 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
EXPOSE 50502
ENTRYPOINT ["./Table.Agg"]

FROM runtime AS agg-hand
COPY --from=build-hand --chown=angzarr:angzarr /out ./
ENV PORT=50503 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
EXPOSE 50503
ENTRYPOINT ["./Hand.Agg"]

# ============================================================================
# Sagas
# ============================================================================
FROM runtime AS saga-table-hand
COPY --from=build-saga-table-hand --chown=angzarr:angzarr /out ./
ENV PORT=50511 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
EXPOSE 50511
ENTRYPOINT ["./Table.SagaHand"]

FROM runtime AS saga-table-player
COPY --from=build-saga-table-player --chown=angzarr:angzarr /out ./
ENV PORT=50512 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
EXPOSE 50512
ENTRYPOINT ["./Table.SagaPlayer"]

FROM runtime AS saga-hand-table
COPY --from=build-saga-hand-table --chown=angzarr:angzarr /out ./
ENV PORT=50513 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
EXPOSE 50513
ENTRYPOINT ["./Hand.SagaTable"]

FROM runtime AS saga-hand-player
COPY --from=build-saga-hand-player --chown=angzarr:angzarr /out ./
ENV PORT=50514 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
EXPOSE 50514
ENTRYPOINT ["./Hand.SagaPlayer"]

# ============================================================================
# Process Manager
# ============================================================================
FROM runtime AS pmg-hand-flow
COPY --from=build-pmg-hand-flow --chown=angzarr:angzarr /out ./
ENV PORT=50591 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
EXPOSE 50591
ENTRYPOINT ["./HandFlow"]

# ============================================================================
# Projector
# ============================================================================
FROM runtime AS prj-output
COPY --from=build-prj-output --chown=angzarr:angzarr /out ./
ENV PORT=50590 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1
EXPOSE 50590
ENTRYPOINT ["./PrjOutput"]
