# VibeChat Worker — multi-stage build (.NET 10)
# Build from repository root: docker build -f apps/worker/Dockerfile .

ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG DOTNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0

FROM ${DOTNET_SDK_IMAGE} AS restore
WORKDIR /src

COPY apps/worker/VibeChat.Worker.csproj apps/worker/
COPY src/VibeChat.SharedKernel/VibeChat.SharedKernel.csproj src/VibeChat.SharedKernel/
COPY src/VibeChat.Infrastructure/VibeChat.Infrastructure.csproj src/VibeChat.Infrastructure/
COPY modules/BuildingBlocks/VibeChat.BuildingBlocks.csproj modules/BuildingBlocks/
COPY modules/Messaging/VibeChat.Messaging.csproj modules/Messaging/
COPY modules/Realtime/VibeChat.Realtime.csproj modules/Realtime/

RUN dotnet restore apps/worker/VibeChat.Worker.csproj

FROM restore AS build
COPY . .
RUN dotnet publish apps/worker/VibeChat.Worker.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM ${DOTNET_RUNTIME_IMAGE} AS final
WORKDIR /app

ENV DOTNET_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

RUN useradd --create-home --uid 10001 --shell /usr/sbin/nologin appuser

COPY --from=build /app/publish .
USER appuser

# No HTTP surface — process liveness (PID 1) for Compose profile apps (B-074).
HEALTHCHECK --interval=15s --timeout=5s --start-period=20s --retries=6 \
  CMD kill -0 1 || exit 1

ENTRYPOINT ["dotnet", "VibeChat.Worker.dll"]
