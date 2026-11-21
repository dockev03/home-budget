# syntax=docker/dockerfile:1

# 1. Front : compilation de la PWA (Vue + Vuetify) vers src/HomeBudget.Api/wwwroot
FROM node:22-alpine AS web
WORKDIR /src/src/HomeBudget.Web
COPY src/HomeBudget.Web/package.json src/HomeBudget.Web/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY src/HomeBudget.Web/ ./
RUN npm run build

# 2. API : publication .NET avec la PWA incluse
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS api
WORKDIR /src
COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY src/HomeBudget.Api/HomeBudget.Api.csproj src/HomeBudget.Api/
RUN dotnet restore src/HomeBudget.Api/HomeBudget.Api.csproj
COPY src/HomeBudget.Api/ src/HomeBudget.Api/
COPY --from=web /src/src/HomeBudget.Api/wwwroot src/HomeBudget.Api/wwwroot
RUN dotnet publish src/HomeBudget.Api/HomeBudget.Api.csproj -c Release -o /app --no-restore /p:UseAppHost=false

# 3. Exécution : image runtime minimale, utilisateur non root (app, UID 1654)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
ENV ASPNETCORE_HTTP_PORTS=8080 \
    HB__DataPath=/data \
    TZ=Europe/Paris \
    DOTNET_CLI_TELEMETRY_OPTOUT=1
RUN mkdir -p /data && chown app:app /data
WORKDIR /app
COPY --from=api /app ./
USER app
VOLUME ["/data"]
EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=5s --start-period=30s --retries=3 \
  CMD ["dotnet", "HomeBudget.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "HomeBudget.Api.dll"]
