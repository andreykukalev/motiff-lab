# The Motiff Lab API (decision record 0014). Built from the repository root, since it uses music-core:
#   docker build -f src/MotiffLab.Api/Dockerfile -t motiff-api .
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json Directory.Build.props .editorconfig ./
COPY src/MotiffLab.MusicCore/MotiffLab.MusicCore.csproj src/MotiffLab.MusicCore/
COPY src/MotiffLab.Api/MotiffLab.Api.csproj src/MotiffLab.Api/
RUN dotnet restore src/MotiffLab.Api/MotiffLab.Api.csproj
COPY src/MotiffLab.MusicCore/ src/MotiffLab.MusicCore/
COPY src/MotiffLab.Api/ src/MotiffLab.Api/
RUN dotnet publish src/MotiffLab.Api/MotiffLab.Api.csproj --configuration Release --no-restore --output /app

# Chiseled: no shell or package manager, runs as a non-root user, listens on 8080.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled
WORKDIR /app
COPY --from=build /app .
EXPOSE 8080
ENTRYPOINT ["dotnet", "MotiffLab.Api.dll"]
