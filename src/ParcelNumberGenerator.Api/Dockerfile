# Production image for the Parcel Number Generator API.
#
# Build context is the repository root, because restore needs the project files of every
# referenced project — build with `docker build -f src/ParcelNumberGenerator.Api/Dockerfile .`

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

# Pinned to the build platform rather than the target: emulating an SDK build under QEMU to
# produce an arm64 image takes an order of magnitude longer than cross-compiling with -a.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Project files and the central version files first, so a source-only change does not
# invalidate the restore layer.
COPY Directory.Build.props Directory.Packages.props global.json ./
COPY src/ParcelNumberGenerator.Api/ParcelNumberGenerator.Api.csproj src/ParcelNumberGenerator.Api/
COPY src/ParcelNumberGenerator.Domain/ParcelNumberGenerator.Domain.csproj src/ParcelNumberGenerator.Domain/
COPY src/ParcelNumberGenerator.Data/ParcelNumberGenerator.Data.csproj src/ParcelNumberGenerator.Data/
COPY src/ParcelNumberGenerator.ServiceDefaults/ParcelNumberGenerator.ServiceDefaults.csproj src/ParcelNumberGenerator.ServiceDefaults/
COPY src/ParcelNumberGenerator.Migrations.PostgreSQL/ParcelNumberGenerator.Migrations.PostgreSQL.csproj src/ParcelNumberGenerator.Migrations.PostgreSQL/
COPY src/ParcelNumberGenerator.Migrations.SqlServer/ParcelNumberGenerator.Migrations.SqlServer.csproj src/ParcelNumberGenerator.Migrations.SqlServer/

RUN dotnet restore src/ParcelNumberGenerator.Api/ParcelNumberGenerator.Api.csproj -a "$TARGETARCH"

COPY src/ src/

WORKDIR /src/src/ParcelNumberGenerator.Api
RUN dotnet publish -c Release -a "$TARGETARCH" -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .

# The aspnet base image ships a non-root `app` user. Running as it costs nothing here
# because the service writes nothing to disk and binds an unprivileged port.
USER app

ENV ASPNETCORE_URLS=http://+:8080
LABEL org.opencontainers.image.source=https://github.com/konradcinkusz/PNG
ENTRYPOINT ["dotnet", "ParcelNumberGenerator.Api.dll"]
