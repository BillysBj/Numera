FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore src/Numera.Worker/Numera.Worker.csproj
RUN dotnet publish src/Numera.Worker/Numera.Worker.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

USER $APP_UID
ENTRYPOINT ["dotnet", "Numera.Worker.dll"]

