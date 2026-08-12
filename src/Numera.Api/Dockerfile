FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore src/Numera.Api/Numera.Api.csproj
RUN dotnet publish src/Numera.Api/Numera.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
RUN mkdir /keys && chown "$APP_UID:$APP_UID" /keys

USER $APP_UID
ENTRYPOINT ["dotnet", "Numera.Api.dll"]

