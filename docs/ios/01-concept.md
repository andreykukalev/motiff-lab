# iOS app concept

Status: proposal, 2026-10-24. The web app is the primary MVP track; this concept describes where an iPad and iPhone app fits and what it must keep.

## Why an iOS app

- **Practice happens at the piano.** Players put a tablet on the music stand, not a laptop. The MVP UX spec (04) already names tablet landscape the main practice layout, and the PRD (NFR-8) targets iOS phones and tablets for the mobile MVP.
- **The iPad is the natural "score on the stand".** A large screen, touch, an optional Pencil and a hardware keyboard on the same device cover listening, reading, selecting and editing.
- **The iPhone is the pocket companion.** Quick listening, looping a motif on the way to a lesson, trying one variation, writing a note in the notebook.
- **Stage 2 needs native capabilities.** Reconstruction attempts need MIDI input from a digital piano and low-latency audio. Safari and the iOS web view have no Web MIDI, and Web Audio input latency is hard to control. A native app is the way to reach both on iOS.
- **Distribution and trust.** An App Store or TestFlight app is easier for study participants to install and keep than a home-screen web app, and iOS does not evict its data the way Safari can evict a website's storage.

## Who it is for

The same people as the web: self-directed pianists, late beginner to intermediate, who can play but struggle to work music out alone. On iOS the device usually sits on the piano and is used with one hand while the other plays, or between attempts. That shapes the design: large targets, one primary action per screen, play and loop always within reach of a thumb, and no step that needs precise pointing.

## What the app does, by stage

| Stage | On the web | On iPad and iPhone |
| --- | --- | --- |
| 1 · Notation motif exploration | The primary product: import MIDI and MusicXML, play, select a motif, read facts, reshape with Keep, edit by hand, export MIDI | The same loop on the same engine, behind a TestFlight build for the pilot and study once the web workspace is stable. Import from Files, AirDrop and "Open in"; export through the share sheet |
| 2 · Guided reconstruction | Desktop Chrome and Edge with Web MIDI | The main platform: MIDI input over USB or Bluetooth (CoreMIDI), low-latency native audio, attempts at the piano |
| 3 · Audio-based reconstruction | Upload a recording | Record or pick a recording on the device; microphone audio processed on the device, as the overview requires |
| 4 · Advanced creative work | Reharmonisation, DAW export | Audio Unit or Inter-App routes to GarageBand and other DAWs, Pencil annotation, more instruments |

Stage 1 on iOS is a second host for an existing product, not a new product. It ships only what the web already has, and adds only what the platform needs (files in and out, touch, audio session, offline storage).

## What stays the same

These are non-negotiable and come from the project's principles and `CLAUDE.md`:

- **One engine.** music-core is the only place that parses, analyses, selects, transforms, checks Keep, explains and edits. The app never reimplements a rule, in Swift or in JavaScript. Same input, parameters and seed give the same notes and diff on the web and on iOS.
- **The UI only displays music-core output.** It never changes notes itself; corrections go through `NoteEditLog`, variation edits through `VariationChain.Edit`, selections through `MotifSelector`.
- **One event model and one workspace,** whatever the input: the same projects, motifs, variations and notebook, and the same project file (`.motiff.json`) that the web exports and imports.
- **Learning over transcription.** Progressive disclosure is the same: help comes on request.
- **Technical honesty.** Detected, inferred, uncertain and user-confirmed look different, by more than colour. Generated notation says it is generated.
- **AI is optional.** Off by default, every feature works with it off, and with it on only the fact digest leaves the device (decision record 0003).
- **Nothing the player imports leaves the device in Stage 1.** No account, no sync, no cloud backup run by Motiff Lab.

## What the app must never do

- Import from Apple Music, streaming services or video sites, or read protected media from the music library. Files only, as on the web.
- Upload, share or publish an imported piece, a motif or a variation. Sharing a MIDI export goes to the player's own destination through the system share sheet, for personal practice, as the terms say.
- Add third-party analytics, advertising or tracking SDKs. The app needs no App Tracking Transparency prompt because it does not track.
- Present inferred hands, voices, rhythm restoration or generated notation as imported fact.

## Success for the iOS track

- A player can import a file on an iPad, select a motif with a finger, hear it loop with no gap, try three variations and keep one, without a mouse, a keyboard or a tutorial.
- The same project opens on the web and on the iPad (by export and import) and gives the same notes, facts and variations.
- Stage 2 attempts on iOS reach the latency targets in [03-technical-design.md](03-technical-design.md).
- Players use the app at the piano: in the study, most iOS sessions happen in landscape on an iPad, or with the iPhone in landscape on the stand.

## Open product questions

- [ ] Is the Stage 1 study run on the web, on iPad TestFlight, or both? Recommendation: web first, as planned; offer the iPad build to participants who practise with a tablet once it passes its exit criteria.
- [ ] iPad-only first, or iPad and iPhone together? Recommendation: iPad first (it fits the practice layout and the workspace as built), iPhone second.
- [ ] Price and plan on the App Store. Stage 1 is free; subscriptions arrive with accounts in Stage 2 and need App Store in-app purchase for digital features.
- [ ] Lithuanian alongside English in the first build? The web question in Stage 1 applies here too.
