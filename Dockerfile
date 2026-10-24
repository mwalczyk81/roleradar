# syntax=docker/dockerfile:1

# Stage 1: Build .NET applications
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0-noble@sha256:747cbade5bfd918a175ac32f8f1d931edec2c7d0d5397a3de72eaef1ac1f66d2 AS build
WORKDIR /src

# Copy project files and restore with locked dependencies
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/RoleRadar.Domain/RoleRadar.Domain.csproj src/RoleRadar.Domain/packages.lock.json src/RoleRadar.Domain/
COPY src/RoleRadar.Application/RoleRadar.Application.csproj src/RoleRadar.Application/packages.lock.json src/RoleRadar.Application/
COPY src/RoleRadar.Infrastructure/RoleRadar.Infrastructure.csproj src/RoleRadar.Infrastructure/packages.lock.json src/RoleRadar.Infrastructure/
COPY src/RoleRadar.Cli/RoleRadar.Cli.csproj src/RoleRadar.Cli/packages.lock.json src/RoleRadar.Cli/
COPY src/RoleRadar.Mcp.Stdio/RoleRadar.Mcp.Stdio.csproj src/RoleRadar.Mcp.Stdio/packages.lock.json src/RoleRadar.Mcp.Stdio/

RUN dotnet restore src/RoleRadar.Cli/RoleRadar.Cli.csproj --locked-mode && \
    dotnet restore src/RoleRadar.Mcp.Stdio/RoleRadar.Mcp.Stdio.csproj --locked-mode

# Copy source code and publish framework-dependent binaries
COPY src/ src/

RUN dotnet publish src/RoleRadar.Mcp.Stdio/RoleRadar.Mcp.Stdio.csproj -c Release --no-restore -o /app/mcp && \
    dotnet publish src/RoleRadar.Cli/RoleRadar.Cli.csproj -c Release --no-restore -o /app/cli

# Stage 2: Download MongoDB Database Tools 100.19.1 and extract Kerberos shared libraries
FROM ubuntu:26.04@sha256:c098726dd9b9733f3e885f8de0ef375206a311b0a317eadc40d347b1115fb944 AS tools-downloader
ARG TARGETARCH
# hadolint ignore=DL3008
RUN apt-get update && apt-get install -y --no-install-recommends ca-certificates curl libgssapi-krb5-2 && rm -rf /var/lib/apt/lists/*
WORKDIR /tools

SHELL ["/bin/bash", "-o", "pipefail", "-c"]
RUN set -eux; \
    if [ "$TARGETARCH" = "arm64" ]; then \
        TOOL_ARCH="arm64"; \
        TOOL_SHA256="ebf518d9a303b36e612e40be106cd0ecc7135dc15d098626f81ac3ee8654a821"; \
        LIB_ARCH="aarch64-linux-gnu"; \
    else \
        TOOL_ARCH="x86_64"; \
        TOOL_SHA256="f34b84a67ab96b4ff52571ec5d15e2148d96cb140b1ea9ef8c29ac33c48e3480"; \
        LIB_ARCH="x86_64-linux-gnu"; \
    fi; \
    curl -sSL "https://fastdl.mongodb.org/tools/db/mongodb-database-tools-ubuntu2404-${TOOL_ARCH}-100.19.1.tgz" -o tools.tgz; \
    echo "${TOOL_SHA256}  tools.tgz" | sha256sum -c -; \
    tar -xzf tools.tgz --strip-components=1; \
    mkdir -p /out/usr/local/bin /out/usr/lib /out/usr/lib/${LIB_ARCH} /out/var/lib/dpkg; \
    install -m 0755 bin/mongodump /out/usr/local/bin/; \
    install -m 0755 bin/mongorestore /out/usr/local/bin/; \
    cp -d /usr/lib/${LIB_ARCH}/libgssapi_krb5.so* /out/usr/lib/; \
    cp -d /usr/lib/${LIB_ARCH}/libgssapi_krb5.so* /out/usr/lib/${LIB_ARCH}/; \
    cp -d /usr/lib/${LIB_ARCH}/libkrb5.so* /out/usr/lib/; \
    cp -d /usr/lib/${LIB_ARCH}/libkrb5.so* /out/usr/lib/${LIB_ARCH}/; \
    cp -d /usr/lib/${LIB_ARCH}/libk5crypto.so* /out/usr/lib/; \
    cp -d /usr/lib/${LIB_ARCH}/libk5crypto.so* /out/usr/lib/${LIB_ARCH}/; \
    cp -d /usr/lib/${LIB_ARCH}/libcom_err.so* /out/usr/lib/; \
    cp -d /usr/lib/${LIB_ARCH}/libcom_err.so* /out/usr/lib/${LIB_ARCH}/; \
    cp -d /usr/lib/${LIB_ARCH}/libkrb5support.so* /out/usr/lib/; \
    cp -d /usr/lib/${LIB_ARCH}/libkrb5support.so* /out/usr/lib/${LIB_ARCH}/; \
    cp -d /usr/lib/${LIB_ARCH}/libkeyutils.so* /out/usr/lib/; \
    cp -d /usr/lib/${LIB_ARCH}/libkeyutils.so* /out/usr/lib/${LIB_ARCH}/; \
    dpkg-query -s libgssapi-krb5-2 libkrb5-3 libk5crypto3 libkrb5support0 libkeyutils1 libcom-err2 > /out/var/lib/dpkg/status

# Stage 3: MCP Runtime (roleradar-mcp)
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled@sha256:7ac579eeb88591c261c85576d7094e8f65949ff9e9502cdaab1de2457e506505 AS mcp
WORKDIR /app
COPY --from=build --chown=1654:1654 /app/mcp /app
USER 1654:1654
ENV DOTNET_EnableDiagnostics=0 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_gcServer=0
ENTRYPOINT ["/app/RoleRadar.Mcp.Stdio"]

# Stage 4: CLI Runtime (roleradar-cli)
FROM mcr.microsoft.com/dotnet/runtime:10.0-noble-chiseled@sha256:7ac579eeb88591c261c85576d7094e8f65949ff9e9502cdaab1de2457e506505 AS cli
COPY --from=tools-downloader /out/usr/ /usr/
COPY --from=tools-downloader /out/var/ /var/
WORKDIR /app
COPY --from=build --chown=1654:1654 /app/cli /app
USER 1654:1654
ENV DOTNET_EnableDiagnostics=0 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 \
    DOTNET_gcServer=0
ENTRYPOINT ["/app/RoleRadar.Cli"]
