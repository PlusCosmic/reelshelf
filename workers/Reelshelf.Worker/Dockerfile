# The transcode worker (docs/video-pipeline/workers-and-jobs.md). Build from the repository root:
#   docker build -f workers/Reelshelf.Worker/Dockerfile -t reelshelf-worker .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["workers/Reelshelf.Worker/Reelshelf.Worker.csproj", "workers/Reelshelf.Worker/"]
RUN dotnet restore "workers/Reelshelf.Worker/Reelshelf.Worker.csproj"

COPY ["workers/Reelshelf.Worker/", "workers/Reelshelf.Worker/"]
RUN dotnet publish "workers/Reelshelf.Worker/Reelshelf.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish \
    --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0-noble AS final
USER root
# Ubuntu 24.04's ffmpeg is built with SVT-AV1, Opus and zimg (zscale, for tone-mapping HDR). The build fails
# if any of them is missing, rather than shipping a worker that refuses to start or refuses HDR.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/* \
    && ffmpeg -hide_banner -encoders > /tmp/encoders \
    && ffmpeg -hide_banner -filters > /tmp/filters \
    && grep -E '^ V\S* libsvtav1 ' /tmp/encoders \
    && grep -E '^ A\S* libopus ' /tmp/encoders \
    && grep -E '^ \S+ zscale ' /tmp/filters \
    && grep -E '^ \S+ tonemap ' /tmp/filters \
    && rm /tmp/encoders /tmp/filters \
    && mkdir -p /work \
    && chown "$APP_UID" /work

# The runtime image's unprivileged user. Jobs work under /work, which can be a tmpfs or volume when the root
# filesystem is read-only.
USER $APP_UID
ENV Worker__WorkDirectory=/work \
    DOTNET_EnableDiagnostics=0
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Reelshelf.Worker.dll"]
