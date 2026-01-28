# Stage 1: Build Frontend
FROM oven/bun:1.3.5 AS frontend-build
WORKDIR /build

# Copy workspace files for dependency resolution
COPY package.json bun.lock ./
COPY packages/ ./packages/
COPY apps/ ./apps/
COPY services/clips/frontend/ ./services/clips/frontend/

# Install and build
WORKDIR /build/services/clips/frontend
RUN bun install --frozen-lockfile
RUN bun run build

# Stage 2: Build .NET
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS dotnet-build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["services/clips/api/Nucleus.Clips.csproj", "services/clips/api/"]
COPY ["packages/nucleus-shared/Nucleus.Shared.csproj", "packages/nucleus-shared/"]
RUN dotnet restore "services/clips/api/Nucleus.Clips.csproj"

COPY ["services/clips/api/", "services/clips/api/"]
COPY ["packages/nucleus-shared/", "packages/nucleus-shared/"]

WORKDIR "/src/services/clips/api"
RUN dotnet publish -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Stage 3: Final Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS final
USER root
RUN apt-get update && apt-get install -y ffmpeg && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app

COPY --from=dotnet-build /app/publish .
COPY --from=frontend-build /build/services/clips/frontend/dist ./wwwroot

EXPOSE 8080
ENTRYPOINT ["dotnet", "Nucleus.Clips.dll"]
