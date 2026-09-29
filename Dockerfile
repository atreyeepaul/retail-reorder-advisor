# Multi-stage build for the Reorder Advisor web app (framework-dependent, .NET 10).
# Build:  docker build -t reorder-web .
# Run:    docker run -p 8080:8080 -e Storage__ConnectionString="<azurite>" reorder-web
# (docker-compose.yml wires this up with an Azurite sidecar for a one-command local run.)

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first, on just the build config + project files, so this layer caches until they change.
COPY global.json nuget.config Directory.Build.props Directory.Packages.props ./
COPY src/Reorder.Core/Reorder.Core.csproj src/Reorder.Core/
COPY src/Reorder.Web/Reorder.Web.csproj src/Reorder.Web/
RUN dotnet restore src/Reorder.Web/Reorder.Web.csproj

# Copy the sources and publish (the web app references Reorder.Core only).
COPY src/Reorder.Core/ src/Reorder.Core/
COPY src/Reorder.Web/ src/Reorder.Web/
RUN dotnet publish src/Reorder.Web/Reorder.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
# ASP.NET Core listens on 8080 by default in the .NET 8+ container images.
EXPOSE 8080
ENTRYPOINT ["dotnet", "Reorder.Web.dll"]
