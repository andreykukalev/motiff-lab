# 0001 · Technology stack

Status: accepted, 2026-10-22. Extended by 0003, which adds a thin AI gateway in Azure Functions.

## Context

Stage 1 (notation-based motif exploration) runs fully in the browser: files are parsed, analysed, transformed and played on the device, and only anonymous analytics leave it. The specification left two questions open: music-core in Rust or TypeScript, and Flutter or React Native for mobile. The team is one .NET developer and Azure solution architect, and the Azure budget for the MVP is $150 a month.

## Decision

- **music-core** is a C# class library (.NET 10). Operations are pure, seeded functions with xUnit tests. The same library runs in the browser (Blazor WebAssembly), on the server (Azure Functions, from Stage 2) and on mobile (.NET MAUI, from Stage 2).
- **Web app** is a standalone Blazor WebAssembly PWA with no server needed for the core loop.
- **Browser-only APIs** (Web Audio with an AudioWorklet, Web MIDI, Canvas 2D for the piano roll and keyboard, IndexedDB) live in a thin TypeScript layer called through JS interop. C# passes whole note lists, so timing-critical scheduling stays in JavaScript.
- **Hosting** is Azure Static Web Apps on the Free plan, with managed Functions (.NET isolated) for the analytics endpoint, writing to Table Storage. Region: West Europe, the only EU region Static Web Apps offers. Infrastructure is defined in Bicep under `infra/`.
- **CI** is GitHub Actions: format check, build, tests and a publish of the web app on every pull request.

## Consequences

- Supersedes the Rust music-core, Flutter UI and FastAPI choices in `docs/05-domain-architecture.md`, and closes the matching open questions in `docs/00-overview.md`.
- Stage 1 hosting costs an estimated $0–5 a month. Stage 3 GPU analysis is the main budget risk; options are Azure Container Apps scaled to zero (serverless GPU where available) or CPU inference through ONNX Runtime.
- The Blazor download is larger than a plain JavaScript app. Use trimming and the `wasm-tools` workload; turn on AOT only if operation latency misses the 300 ms preview target (S1-E3).
- Low-latency audio on mobile needs native work in MAUI; prove it with a spike before Stage 2 is committed.
