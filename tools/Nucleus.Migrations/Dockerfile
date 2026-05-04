FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["tools/Nucleus.Migrations/Nucleus.Migrations.csproj", "tools/Nucleus.Migrations/"]
RUN dotnet restore "tools/Nucleus.Migrations/Nucleus.Migrations.csproj"

COPY ["tools/Nucleus.Migrations/", "tools/Nucleus.Migrations/"]

WORKDIR "/src/tools/Nucleus.Migrations"
RUN dotnet publish -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Nucleus.Migrations.dll"]
