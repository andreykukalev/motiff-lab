# Motiff Lab

**Hear. Understand. Transform.**

Motiff Lab is a musical exploration workspace that helps musicians understand the music they love, reconstruct musical ideas, and create their own variations.

The first release (Stage 1) is a browser app for notation-based motif exploration: import a MIDI or MusicXML file, pick a motif, explore its rhythm and melody, and transform it with controlled, explained variations.

## Development

Requires the .NET 10 SDK.

```bash
dotnet test MotiffLab.slnx
dotnet run --project src/MotiffLab.Web
```

The stack is described in [docs/decisions/0001-stack.md](docs/decisions/0001-stack.md).

## Documentation

All product and technical specifications are in [`docs/`](docs/README.md). Start with the [Overview](docs/00-overview.md), then the [Stage 1 MVP](docs/01-mvp-stage-1.md).
