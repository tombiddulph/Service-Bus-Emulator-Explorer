FROM mcr.microsoft.com/dotnet/runtime-deps:10.0-noble-chiseled AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build

# Install Node.js 20 via NodeSource + AOT dependencies
RUN apt-get update && \
    apt-get install -y --no-install-recommends ca-certificates curl clang zlib1g-dev && \
    curl -fsSL https://deb.nodesource.com/setup_20.x | bash - && \
    apt-get install -y --no-install-recommends nodejs && \
    rm -rf /var/lib/apt/lists/*

ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Restore .NET dependencies with RID for AOT
COPY ["src/ServiceBusEmulatorExplorer/ServiceBusEmulatorExplorer.csproj", "src/ServiceBusEmulatorExplorer/"]
RUN dotnet restore "src/ServiceBusEmulatorExplorer/ServiceBusEmulatorExplorer.csproj" -r linux-x64

# Restore npm dependencies
COPY ["app/sb-explorer-ui/package.json", "app/sb-explorer-ui/"]
COPY ["app/sb-explorer-ui/package-lock.json", "app/sb-explorer-ui/"]
WORKDIR /src/app/sb-explorer-ui
RUN npm ci --include=dev

# Copy everything else
WORKDIR /src
COPY . .

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
ARG VERSION=0.0.0
ARG ASSEMBLY_VERSION=0.0.0.0
WORKDIR /src/src/ServiceBusEmulatorExplorer
RUN dotnet publish "ServiceBusEmulatorExplorer.csproj" -c $BUILD_CONFIGURATION -r linux-x64 \
    -p:Version=${VERSION} \
    -p:AssemblyVersion=${ASSEMBLY_VERSION} \
    -p:FileVersion=${ASSEMBLY_VERSION} \
    -p:InformationalVersion=${VERSION} \
    -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

ENV ASPNETCORE_URLS=http://+:8080

ENTRYPOINT ["./ServiceBusEmulatorExplorer.Api"]