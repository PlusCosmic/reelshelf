# Stage 1: Build Frontend
# Cache bust: 2026-01-29-001
FROM oven/bun:1.3.13 AS frontend-build
WORKDIR /build

# Copy workspace files for dependency resolution
COPY package.json bun.lock ./
COPY frontend/ ./frontend/

# Install and build
WORKDIR /build
RUN bun install --frozen-lockfile
RUN bun run --cwd frontend build

# Stage 2: Build .NET
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS dotnet-build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["api/Reelshelf.csproj", "api/"]
RUN dotnet restore "api/Reelshelf.csproj"

COPY ["api/", "api/"]
COPY ["migrations/", "migrations/"]

WORKDIR "/src/api"
RUN dotnet publish -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Stage 3: Final Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
USER root
RUN apt-get update && apt-get install -y ffmpeg && rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app

COPY --from=dotnet-build /app/publish .
COPY --from=frontend-build /build/frontend/dist ./wwwroot

EXPOSE 8080
ENTRYPOINT ["dotnet", "Reelshelf.dll"]
