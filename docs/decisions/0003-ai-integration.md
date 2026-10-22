# 0003 · AI integration through Azure AI Foundry

Status: proposed, 2026-10-22. It becomes accepted once the milestone 2 spike confirms the hosting details marked "verify".

## Context

Stage 1 adds AI assistance (`docs/01-mvp-stage-1.md`, AI assistance): explanations, variation suggestions, rhythm-restoration recommendations, motif relationships, and questions, hints and feedback in the learning modes. The deterministic music-core stays responsible for notes, timing, analysis, transformations and Keep checks; AI interprets its results.

Constraints:
- The app is a standalone Blazor WebAssembly PWA on Azure Static Web Apps Free (0001).
- The core journey must work offline and without AI.
- Imported files stay on the device. Only a compact fact digest may be sent, after consent.
- The Azure budget is $150 a month for the whole MVP.
- One developer, so the backend must stay small.
- Model and provider must be replaceable.

## Options considered

| Option | Verdict |
| --- | --- |
| Browser calls Azure AI Foundry directly | Rejected. The browser would hold a key or token anyone can extract, and there would be no server-side validation, rate limiting or budget control |
| Thin gateway as Azure Functions behind Static Web Apps | **Chosen.** Keeps credentials server-side, validates requests and responses, and controls usage. Free hosting tier, scales to zero, and is .NET like the rest of the stack |
| Separate ASP.NET Core API on App Service or Container Apps | Rejected for Stage 1. It costs more and needs more operations work, and adds nothing the gateway needs today. Revisit in Stage 2, when accounts and sync need a real API |
| Agent framework or orchestration service | Rejected. Each feature is one request with a fixed schema; there is no multi-step tool use to orchestrate |

## Decision

1. **Gateway.** A .NET isolated Azure Functions project, `MotiffLab.Api`, serves `POST /api/assistant/{feature}`. Start with Static Web Apps managed functions on the Free plan. The model key lives in the Static Web App's application settings and never reaches the browser.
   - **Verify:** managed functions do not support managed identity. When moving to keyless access (managed identity to Foundry, secrets in Key Vault), link a standalone Function App, which needs the Static Web Apps Standard plan (about $9 a month).
2. **Contracts.** A small `MotiffLab.Assistant.Contracts` library holds the request and response types and the per-feature JSON schemas. Both the web app and the gateway use it. music-core builds the fact digest and validates operation proposals; it has no dependency on AI or Azure.
3. **Replaceable model.** The gateway calls the model through one interface, `IAssistantModel`, with a single Azure AI Foundry adapter that uses the Azure AI inference client. Model name, endpoint and prompt version are configuration. Switching model or provider means a new adapter or new settings, not new features.
4. **Request validation.** Every request must:
   - match the feature schema;
   - be at most 8 KB;
   - carry a digest with fact IDs, an engine version and a random installation ID;
   - never contain file content, file names or note lists beyond the digest.
5. **Response validation.**
   - The model must answer in JSON that matches the feature schema (structured output).
   - The gateway rejects answers that cite fact IDs missing from the digest, propose operations outside the catalogue or parameters outside their ranges, or exceed length limits.
   - On rejection, timeout (5 s) or error, it returns `{ "fallback": true }`, and the app shows its template text.
6. **Usage control.**
   - Per-installation rate limits: 30 requests an hour, 200 a day.
   - A global daily token budget, with counters in Azure Table Storage.
   - An Azure budget alert on the resource group.
   - Proposed AI cap: $20 a month for Stage 1.
   - For the study, access requires an invite code checked by the gateway (see open questions).
7. **Data protection.**
   - The Foundry deployment runs in an EU region (Sweden Central or another EU region with the chosen model).
   - The gateway logs only feature, prompt version, token counts, latency and outcome, never prompt or answer text.
   - The privacy notice states the provider's retention and abuse-monitoring terms for the deployment. **Verify** them for the chosen deployment type before the study.
8. **Prompts and evaluation.**
   - Prompts are versioned files in the repository.
   - An evaluation set of 30 motifs, with engine facts and expected properties, runs before any prompt or model change.
   - A change ships only if statement accuracy stays at or above 95% and validation failures at or below 5%.

## Request flow

```
Blazor app ──(consent on)──> music-core builds FactDigest
          ──POST /api/assistant/explain {digest, mode, keep, installId, inviteCode}──> Functions gateway
gateway: schema + size check → rate limit + budget → prompt v-N + digest → IAssistantModel (Foundry)
       → JSON schema check → fact-ID and catalogue check → response or {fallback:true}
Blazor app: shows AI text labelled as interpretation; applying a suggestion runs it in music-core,
            which writes the factual explanation from the diff
```

## Cost estimate

Each request sends about 1,500–2,500 tokens and receives about 300–600. With a small, low-cost model from the Foundry catalogue, that is a fraction of a cent per request. The study, with 12 participants and a week at home, should stay in single-digit dollars a month. Functions and Table Storage stay within free grants at this volume.

These figures are estimates; confirm prices for the chosen model and region in the Azure pricing calculator. The $20 cap is a guard, not a forecast.

## Consequences

- Stage 1 gains a small backend, so "no backend required" in 0001 now means "none for the core journey". Hosting stays on the Free plan unless keyless access is required.
- AI features can be switched off centrally (the gateway returns fallback) or per player, without changing the app.
- The same gateway and grounding rules serve the Stage 3 AI tutor (`POST /tutor/explain` in `05-domain-architecture.md`).
- Open questions: how to gate access outside the study (sign-in through Static Web Apps, or anonymous use with rate limits), which model to deploy, and whether Lithuanian is needed in Stage 1.
