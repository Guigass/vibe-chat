# VibeChat API — multi-stage build (.NET 10)
# Build from repository root: docker build -f apps/api/Dockerfile .

ARG DOTNET_SDK_IMAGE=mcr.microsoft.com/dotnet/sdk:10.0
ARG DOTNET_RUNTIME_IMAGE=mcr.microsoft.com/dotnet/aspnet:10.0

FROM ${DOTNET_SDK_IMAGE} AS restore
WORKDIR /src

COPY VibeChat.slnx ./
COPY apps/api/VibeChat.Api.csproj apps/api/
COPY apps/worker/VibeChat.Worker.csproj apps/worker/
COPY src/VibeChat.SharedKernel/VibeChat.SharedKernel.csproj src/VibeChat.SharedKernel/
COPY src/VibeChat.Infrastructure/VibeChat.Infrastructure.csproj src/VibeChat.Infrastructure/
COPY modules/BuildingBlocks/VibeChat.BuildingBlocks.csproj modules/BuildingBlocks/
COPY modules/Identity/VibeChat.Identity.csproj modules/Identity/
COPY modules/Tenancy/VibeChat.Tenancy.csproj modules/Tenancy/
COPY modules/Directory/VibeChat.Directory.csproj modules/Directory/
COPY modules/Conversations/VibeChat.Conversations.csproj modules/Conversations/
COPY modules/Messaging/VibeChat.Messaging.csproj modules/Messaging/
COPY modules/Realtime/VibeChat.Realtime.csproj modules/Realtime/
COPY modules/Files/VibeChat.Files.csproj modules/Files/
COPY modules/Search/VibeChat.Search.csproj modules/Search/
COPY modules/Notifications/VibeChat.Notifications.csproj modules/Notifications/
COPY modules/Administration/VibeChat.Administration.csproj modules/Administration/
COPY modules/Audit/VibeChat.Audit.csproj modules/Audit/
COPY modules/Moderation/VibeChat.Moderation.csproj modules/Moderation/
COPY modules/Integrations/VibeChat.Integrations.csproj modules/Integrations/
COPY modules/AI/VibeChat.AI.csproj modules/AI/

RUN dotnet restore apps/api/VibeChat.Api.csproj

FROM restore AS build
COPY . .
RUN dotnet publish apps/api/VibeChat.Api.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

FROM ${DOTNET_RUNTIME_IMAGE} AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:5080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_EnableDiagnostics=0

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/ \
    && useradd --create-home --uid 10001 --shell /usr/sbin/nologin appuser

COPY --from=build /app/publish .
USER appuser
EXPOSE 5080

HEALTHCHECK --interval=10s --timeout=5s --start-period=20s --retries=6 \
  CMD curl -f http://127.0.0.1:5080/health || exit 1

ENTRYPOINT ["dotnet", "VibeChat.Api.dll"]
