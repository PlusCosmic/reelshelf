FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Copy project files for restore (leverages Docker layer caching)
COPY ["Nucleus.Minecraft/Nucleus.Minecraft.csproj", "Nucleus.Minecraft/"]
COPY ["Nucleus.Shared/Nucleus.Shared.csproj", "Nucleus.Shared/"]
RUN dotnet restore "Nucleus.Minecraft/Nucleus.Minecraft.csproj"
# Copy all source code
COPY ["Nucleus.Minecraft/", "Nucleus.Minecraft/"]
COPY ["Nucleus.Shared/", "Nucleus.Shared/"]
# Build
WORKDIR "/src/Nucleus.Minecraft"
RUN dotnet build "Nucleus.Minecraft.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Nucleus.Minecraft.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Nucleus.Minecraft.dll"]
