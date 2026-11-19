# The Motiff Lab API (decision record 0014). Built from the repository root, since it uses music-core:
#   docker build -f src/backend/MotiffLab.Api/Dockerfile -t motiff-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props .editorconfig ./
COPY src/backend/MotiffLab.MusicCore/MotiffLab.MusicCore.csproj src/backend/MotiffLab.MusicCore/
COPY src/backend/MotiffLab.Api/MotiffLab.Api.csproj src/backend/MotiffLab.Api/
RUN dotnet restore src/backend/MotiffLab.Api/MotiffLab.Api.csproj
COPY src/backend/MotiffLab.MusicCore/ src/backend/MotiffLab.MusicCore/
COPY src/backend/MotiffLab.Api/ src/backend/MotiffLab.Api/
RUN dotnet publish src/backend/MotiffLab.Api/MotiffLab.Api.csproj --configuration Release --no-restore --output /app

# Chiseled: no shell or package manager, runs as a non-root user, listens on 8080.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "MotiffLab.Api.dll"]
