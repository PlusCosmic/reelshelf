# The transcode worker (docs/video-pipeline/workers-and-jobs.md). Build from the repository root:
#   docker build -f workers/Reelshelf.Worker/Dockerfile -t reelshelf-worker .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["workers/Reelshelf.Worker/Reelshelf.Worker.csproj", "workers/Reelshelf.Worker/"]
RUN dotnet restore "workers/Reelshelf.Worker/Reelshelf.Worker.csproj" -r linux-x64

COPY ["workers/Reelshelf.Worker/", "workers/Reelshelf.Worker/"]
# Self-contained, so the final stage can be plain Debian: Microsoft's .NET 10 runtime images are Ubuntu 24.04
# (there is no stable Debian 13 tag), and Ubuntu's SVT-AV1 is the 1.7 that crashes on the dev server. The worker
# formats everything with the invariant culture, so it needs no ICU.
RUN dotnet publish "workers/Reelshelf.Worker/Reelshelf.Worker.csproj" -c $BUILD_CONFIGURATION -o /app/publish \
    -r linux-x64 --self-contained --no-restore /p:InvariantGlobalization=true

# Debian 13 (trixie): its ffmpeg 7.1 has SVT-AV1 2.3. Ubuntu 24.04's SVT-AV1 1.7.0 segfaulted inside
# libSvtAv1Enc on the dedicated server's VM (which reports one CPU socket per vCPU) for an ordinary 1080p60 H.264
# clip that trixie's 2.3 encodes there without trouble.
FROM debian:trixie-slim AS final
# Debian's ffmpeg is built with SVT-AV1, Opus, zimg (zscale, for tone-mapping HDR) and GnuTLS (https, for the HLS
# playlists migrate jobs read from Bunny's CDN). The build fails if any of them, or the HLS demuxer, is missing, or
# if SVT-AV1 is older than 2.x, rather than shipping a worker that refuses to start, refuses HDR, can't migrate or
# crashes on its first real clip.
RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg ca-certificates tzdata \
    && rm -rf /var/lib/apt/lists/* \
    && ffmpeg -hide_banner -encoders > /tmp/encoders \
    && ffmpeg -hide_banner -filters > /tmp/filters \
    && ffmpeg -hide_banner -protocols | sed '/^Output:/q' > /tmp/protocols \
    && ffmpeg -hide_banner -demuxers > /tmp/demuxers \
    && grep -E '^ V\S* libsvtav1 ' /tmp/encoders \
    && grep -E '^ A\S* libopus ' /tmp/encoders \
    && grep -E '^ \S+ zscale ' /tmp/filters \
    && grep -E '^ \S+ tonemap ' /tmp/filters \
    && grep -x '  https' /tmp/protocols \
    && grep -E '^ D\S* +hls ' /tmp/demuxers \
    && ldconfig -p | grep -E 'libSvtAv1Enc\.so\.[2-9]' \
    && rm /tmp/encoders /tmp/filters /tmp/protocols /tmp/demuxers \
    && groupadd --gid 1654 app \
    && useradd --uid 1654 --gid app --no-create-home --shell /usr/sbin/nologin app \
    && mkdir -p /work \
    && chown app:app /work

# An unprivileged user with the same uid as the .NET images' app user, so an existing /work volume stays
# writable. Jobs work under /work, which can be a tmpfs or volume when the root filesystem is read-only.
USER app
ENV Worker__WorkDirectory=/work \
    DOTNET_EnableDiagnostics=0
WORKDIR /app
COPY --from=build /app/publish .

ENTRYPOINT ["./Reelshelf.Worker"]
