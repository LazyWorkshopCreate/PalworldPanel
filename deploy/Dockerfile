FROM node:24-alpine@sha256:d1b3b4da11eefd5941e7f0b9cf17783fc99d9c6fc34884a665f40a06dbdfc94f AS web
WORKDIR /web
RUN corepack enable
COPY src/PalworldPanel.AdminWeb/package.json src/PalworldPanel.AdminWeb/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
COPY src/PalworldPanel.AdminWeb/ ./
COPY src/PalworldPanel.Contracts/ /PalworldPanel.Contracts/
RUN pnpm build

FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS server
ARG PANEL_VERSION=0.0.0
WORKDIR /source
COPY global.json Directory.Build.props ./
COPY src/PalworldPanel.Server/ src/PalworldPanel.Server/
COPY src/PalworldPanel.Contracts/ src/PalworldPanel.Contracts/
RUN dotnet restore src/PalworldPanel.Server --locked-mode && dotnet publish src/PalworldPanel.Server --no-restore -c Release -p:Version=$PANEL_VERSION -o /app
COPY --from=web /web/dist/ /app/wwwroot/

FROM docker:29-cli@sha256:b1805116a6a86cc591b5d5f60a910a0715cdcc9d18d866ad68b1457ead25c35c AS dockercli
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4
RUN apt-get update && apt-get install -y --no-install-recommends libsqlite3-0 && rm -rf /var/lib/apt/lists/*
COPY --from=dockercli /usr/local/bin/docker /usr/local/bin/docker
COPY --from=dockercli /usr/local/libexec/docker/cli-plugins/docker-compose /usr/local/libexec/docker/cli-plugins/docker-compose
WORKDIR /app
COPY --from=server /app/ ./
ARG PANEL_VERSION=0.0.0
LABEL org.opencontainers.image.version="$PANEL_VERSION"
USER root
LABEL com.palworldpanel.role="control-plane"
ENTRYPOINT ["dotnet", "PalworldPanel.Server.dll"]
