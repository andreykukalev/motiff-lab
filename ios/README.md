# Motiff Lab for iPad

A small Swift app around the deployed Motiff Lab web app (decision record [0015](../docs/decisions/0015-ipad-app.md)). The web app does all the music; this shell adds sound with the silent switch on, a screen that stays on at the piano, the Apple Pencil's double tap and squeeze, export to Files, and an offline start. Writing on the score with the Pencil is part of the web app, so it works in Safari on the iPad too.

Every change to the web app reaches the iPad the next time the app starts with a connection; build the app again only when something in `ios/` changes.

## What you need

- A Mac with Xcode 16 or later (App Store) and [Homebrew](https://brew.sh).
- XcodeGen, which makes the Xcode project from `project.yml`: `brew install xcodegen`.
- An Apple ID added to Xcode (Xcode > Settings > Accounts > +). A free one works: it signs the app for 7 days at a time, then you run it from Xcode again.
- The iPad (iPadOS 17 or later) and a USB-C cable.

## Build and install

```bash
git clone https://github.com/andreykukalev/motiff-lab.git   # or git pull in your clone
cd motiff-lab/ios
cp Config/Local.xcconfig.example Config/Local.xcconfig     # then set DEVELOPMENT_TEAM, see below
xcodegen                                                   # makes MotiffLab.xcodeproj
open MotiffLab.xcodeproj
```

1. **Team.** In Xcode, select the MotiffLab target > Signing & Capabilities and choose your team ("Your Name (Personal Team)" for a free Apple ID). To keep it across `xcodegen` runs, put its ID in `Config/Local.xcconfig`: after choosing it once, `grep -m1 DEVELOPMENT_TEAM MotiffLab.xcodeproj/project.pbxproj` shows it. If Xcode says the bundle ID is not available, set another `PRODUCT_BUNDLE_IDENTIFIER` there.
2. **The iPad.** Connect it, unlock it and tap Trust. Turn on Settings > Privacy & Security > Developer Mode (the iPad restarts). In Xcode, choose the iPad as the run destination at the top of the window.
3. **Run.** Product > Run (⌘R). The first time, the iPad refuses to open an app from an unknown developer: go to Settings > General > VPN & Device Management, tap your Apple ID and Trust. Then open Motiff Lab from the home screen.

After the first run over the cable, Xcode can also install over Wi-Fi when the Mac and the iPad are on the same network (Window > Devices and Simulators > Connect via network).

The first start needs an internet connection. After that the app opens without one, as the web app does once installed.

## Writing on the score

- Write on the score with the Pencil at any time; the **✎ Pencil** button above the score then shows the tools: a pen in black, red or blue, the highlighter, the eraser, Undo and Clear marks.
- A finger scrolls and never writes, so your hand can rest on the screen. A tap with the Pencil on a note plays from it; on a bar away from its notes, it selects the bar.
- A double tap on the Pencil switches to the eraser and back (as set in Settings > Apple Pencil); a squeeze on the Pencil Pro shows or hides the tools.
- Marks stay with the piece on this iPad and follow their bars when the score is laid out again (another width, the other orientation). They are not yet in exported project files or saved online.

## Trying a change

There are no preview sites: a change reaches the app once it is merged and CI has deployed it. The app opens the production site, so it shows the change once it next loads the web app.

## Looking inside the page

In a Debug build, Safari on the Mac can inspect the page: on the iPad turn on Settings > Apps > Safari > Advanced > Web Inspector; on the Mac turn on Safari > Settings > Advanced > Show features for web developers; then Develop > (the iPad) > Motiff Lab.

## Files

- `project.yml`: the XcodeGen project; Info.plist is generated from it (camera and microphone reasons, the app-bound domain).
- `Config/Base.xcconfig`: the web app's host, the bundle ID and the team; `Local.xcconfig` (not committed) overrides them.
- `MotiffLab/MotiffLabApp.swift`: the app and its one screen.
- `MotiffLab/WebViewController.swift`: the web view, audio, links, dialogs, downloads, and the screen when the first start has no connection.
- `MotiffLab/PencilBridge.swift`: the Pencil's double tap and squeeze, passed to the page.
- `MotiffLab/Assets.xcassets`: a placeholder icon (the four-note contour of `docs/02-brand-identity.md`) and the colours.
