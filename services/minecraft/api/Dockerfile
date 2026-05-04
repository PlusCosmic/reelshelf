FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Copy project files for restore (leverages Docker layer caching)
COPY ["services/minecraft/api/Nucleus.Minecraft.csproj", "services/minecraft/api/"]
COPY ["packages/nucleus-shared/Nucleus.Shared.csproj", "packages/nucleus-shared/"]
RUN dotnet restore "services/minecraft/api/Nucleus.Minecraft.csproj"
# Copy all source code
COPY ["services/minecraft/api/", "services/minecraft/api/"]
COPY ["packages/nucleus-shared/", "packages/nucleus-shared/"]
# Build
WORKDIR "/src/services/minecraft/api"
RUN dotnet build "Nucleus.Minecraft.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Nucleus.Minecraft.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Nucleus.Minecraft.dll"]
