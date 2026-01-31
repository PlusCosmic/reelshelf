FROM mcr.microsoft.com/dotnet/aspnet:10.0-preview AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:10.0-preview AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
# Copy project files for restore (leverages Docker layer caching)
COPY ["Nucleus.Clips/Nucleus.Clips.csproj", "Nucleus.Clips/"]
COPY ["Nucleus.Shared/Nucleus.Shared.csproj", "Nucleus.Shared/"]
RUN dotnet restore "Nucleus.Clips/Nucleus.Clips.csproj"
# Copy all source code
COPY ["Nucleus.Clips/", "Nucleus.Clips/"]
COPY ["Nucleus.Shared/", "Nucleus.Shared/"]
# Build
WORKDIR "/src/Nucleus.Clips"
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
