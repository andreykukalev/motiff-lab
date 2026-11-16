# Motiff Lab

**Hear. Understand. Transform.**

Motiff Lab is a musical exploration workspace that helps musicians understand the music they love, reconstruct musical ideas, and create their own variations.

Stage 1 is notation-based motif exploration: import a MIDI or MusicXML file, pick a motif, explore its rhythm and melody, and transform it with controlled, explained variations.

Development is **iPad first** ([0032](docs/architecture/adr/0032-ipad-first-web-archived.md)): the native iPad app ([`src/ipad/`](src/ipad/README.md)) is the only actively developed client, over the shared C# music-core, the API and the scan server. The web app (`src/MotiffLab.Web`, Blazor WebAssembly) is archived in place and recoverable: see [docs/web-archive.md](docs/web-archive.md).

## Development

Requires the .NET 10 SDK, and Node 22 or later for the TypeScript tests.

```bash
dotnet build MotiffLab.slnx   # also compiles the shared TypeScript modules the iPad bundles
dotnet test MotiffLab.slnx
node --test "tests/web/*.test.mjs"
```

The iPad app is built on a Mac (`src/ipad/README.md`).

The stack is described in [docs/architecture/adr/0001-stack.md](docs/architecture/adr/0001-stack.md). The backend's hosting (Azure Container Apps, Cosmos DB, sign-in) and the archived web app's Static Web App are in [infra/README.md](infra/README.md).

## Documentation

All product and technical specifications are in [`docs/`](docs/README.md). Start with the [Overview](docs/00-overview.md), then the [Stage 1 MVP](docs/01-mvp-stage-1.md).
