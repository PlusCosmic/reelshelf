FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Copy project files for restore (leverages Docker layer caching)
COPY ["services/clips/api/Nucleus.Clips.csproj", "services/clips/api/"]
COPY ["packages/nucleus-shared/Nucleus.Shared.csproj", "packages/nucleus-shared/"]
RUN dotnet restore "services/clips/api/Nucleus.Clips.csproj"
# Copy all source code
COPY ["services/clips/api/", "services/clips/api/"]
COPY ["packages/nucleus-shared/", "packages/nucleus-shared/"]
# Build
WORKDIR "/src/services/clips/api"
RUN dotnet build "Nucleus.Clips.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "Nucleus.Clips.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
USER root
RUN apt-get update && \
    apt-get install -y ffmpeg && \
    apt-get clean && \
    rm -rf /var/lib/apt/lists/*
USER $APP_UID
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Nucleus.Clips.dll"]
