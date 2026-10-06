FROM mcr.microsoft.com/dotnet/sdk:10.0@sha256:e70cdb7f80b0348f5cb85f19a8f670fca061f033d57eed12fa003d58b0e06317 AS build
ARG SERVICE
WORKDIR /src
COPY global.json Directory.Build.props NuGet.Config ./
COPY backend ./backend
RUN dotnet restore backend/${SERVICE}/${SERVICE}.csproj --locked-mode
RUN dotnet publish backend/${SERVICE}/${SERVICE}.csproj -c Release --no-restore -o /app/publish
FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:222759b391a1aaf241166672c8f99b2d4ada452e7b5319f3c6e8f265a37b5ad4
ARG SERVICE
WORKDIR /app
COPY --from=build /app/publish .
ENV SERVICE_NAME=${SERVICE} ASPNETCORE_URLS=http://+:8080 DOTNET_EnableDiagnostics=0
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["/bin/sh", "-c", "exec dotnet ${SERVICE_NAME}.dll \"$@\"", "--"]
