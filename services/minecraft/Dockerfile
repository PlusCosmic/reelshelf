# Stage 1: Build Frontend
FROM oven/bun:1.3.5 AS frontend-build
WORKDIR /build

# Copy workspace files for dependency resolution
COPY package.json bun.lock ./
COPY packages/ ./packages/
COPY services/minecraft/frontend/ ./services/minecraft/frontend/

# Install and build
WORKDIR /build/services/minecraft/frontend
RUN bun install --frozen-lockfile
RUN bun run build

# Stage 2: Build .NET
FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS dotnet-build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["services/minecraft/api/Nucleus.Minecraft.csproj", "services/minecraft/api/"]
COPY ["packages/nucleus-shared/Nucleus.Shared.csproj", "packages/nucleus-shared/"]
RUN dotnet restore "services/minecraft/api/Nucleus.Minecraft.csproj"

COPY ["services/minecraft/api/", "services/minecraft/api/"]
COPY ["packages/nucleus-shared/", "packages/nucleus-shared/"]

WORKDIR "/src/services/minecraft/api"
RUN dotnet publish -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

# Stage 3: Final Runtime
FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS final
USER $APP_UID
WORKDIR /app

COPY --from=dotnet-build /app/publish .
COPY --from=frontend-build /build/services/minecraft/frontend/dist ./wwwroot

EXPOSE 8080
ENTRYPOINT ["dotnet", "Nucleus.Minecraft.dll"]
