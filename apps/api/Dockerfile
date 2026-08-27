FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY NuGet.Config ./NuGet.Config
COPY apps/api/TarotDestiny.Api.csproj apps/api/TarotDestiny.Api.csproj
COPY contracts/classifier/v1/classifier.proto contracts/classifier/v1/classifier.proto
RUN dotnet restore apps/api/TarotDestiny.Api.csproj --configfile NuGet.Config

COPY apps/api apps/api
COPY contracts contracts
RUN dotnet publish apps/api/TarotDestiny.Api.csproj \
    --configuration Release \
    --no-restore \
    --output /app/publish \
    /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
RUN apt-get update \
    && apt-get install --yes --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_HTTP_PORTS=5000 \
    DOTNET_EnableDiagnostics=0

USER app
EXPOSE 5000

ENTRYPOINT ["dotnet", "TarotDestiny.Api.dll"]
