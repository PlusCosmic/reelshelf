FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["migrations/Nucleus.Migrations.csproj", "migrations/"]
RUN dotnet restore "migrations/Nucleus.Migrations.csproj"

COPY ["migrations/", "migrations/"]

WORKDIR "/src/migrations"
RUN dotnet publish -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "Nucleus.Migrations.dll"]
