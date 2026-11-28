# Code map: iPad `Account/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Account`. `Account.json` (both environments'
servers) stays at the top of `src/ipad/App/MotiffLab`, where `src/ipad/build-version.sh` sets its "Default".

## Files

`About`, `Account`, `AccountPanel`, `CloudSync`, `Environments`, `OnlineProjects`, `PlaylistsSync`, `ProfileController`,
`ProfileEditor`, `SyncConflict`, `TestLibrary` (`.swift`).

- `Account.swift`: MSAL sign-in, `apiRequest(path)` (the API with the player's token), plan and Pro features, the
  profile (`PlayerProfile`, `saveProfile`), `deleteOnlineData` («Удалить аккаунт и данные онлайн»: DELETE /me, then
  sign out, answering an `OnlineDeletion`; the rule itself in the static `deleteOnlineData(_:signOut:)`; the files and
  sync records here stay, E-settings-5).

### Sync (version 0.2; v1.6 c-17b)

- `CloudSync.swift`: one pass (`run`), music-core deciding each step (`project.sync`). `SyncRecords` (per environment,
  `sync.json` / `sync-test.json`): the ETag, save time and hash last seen, `deletedHere`. `SyncBases`: the copy last
  sent or fetched per piece (`sync-bases/`, `sync-test-bases/`; a piece synced before c-17b gets one while neither side
  changed it, `seedBase`), sent as `project.resolve`'s "base" with "sendsFile": `keep` writes the previous copy, then
  "file" (the copy goes again when "file" cannot be written, E-sync-3); "file" goes online itself when "upload" is true
  (If-Match the fetched ETag), and on the next pass when the merge failed its self-check (E-sync-7). A first upload has
  no If-Match (create only; 412 → resolved next pass). `markDeletedHere` (the library's Delete) and `join` (a copy that
  joined another) mark a piece → "deleteOnline" → DELETE with If-Match (204/404 drop the record and base, 412 keeps
  them); `offered` leaves the marked out of «Нет на этом iPad» (E-sync-2). A later build's copy here
  (`PieceSummary.readOnly`) is never sent; one online stays the piece (decision 39). `Outcome.conflicts`: `SyncConflict` (`SyncConflict.swift`), the
  pieces resolved with a conflict, named by kind from the core's "in" for the library's line (E-sync-4).
- `OnlineProjects.swift`: the API's projects (list, fetch with ETag, `upload` with If-Match, `delete(_:ifMatch:)`,
  `deleteEverything` = DELETE /me), the playlists document (`fetchPlaylists` with ETag and Last-Modified,
  `uploadPlaylists`), «Нет на этом iPad» (`OnlineList`, `OnlineShelf`).
- `PlaylistsSync.swift` (v1.7 p-3): the playlists' step, run by `CloudSync.run` after the pieces when the library gives
  `CloudSync.playlists`. `PlaylistsFileStore` is the library's side (p-4's record implements it: `current`, and
  `replace(_:with:)` only over the file it read); `PlaylistsSyncRecord` (`sync-playlists.json`, `sync-test-playlists.json`:
  ETag, base, `deletedOnline`); the step by `project.sync`'s facts, `playlists.resolve` with the base and the pieces'
  `formerIds` (`rekeyed`, `PieceSummary.formerIds`); a later build's copy never sent; read-only where the app may not write.

### Settings (W2.12, v1.6 i-14)

`Account/SettingsView.swift` was cut by model, controller and views:

- The model: the device's settings, `Platform/DeviceSettings.swift` ([`platform.md`](platform.md)), which eight folders
  read; the account's state stays `Account`'s.
- The sheet: `Workspace/SettingsSheet.swift`, the sections (Guidance, Score, Layers, Piano roll, Appearance, Account,
  About) on the left in landscape, as tabs in portrait; the piece's own choices listed, to clear in one tap. It
  composes the settings with this folder's panels, Import's scan server (`ScanPanel`, the engine's choice) and Marks'
  kinds of extra information, so it is Workspace's. Opened from the library, the score and the PDF (`section: .account`).
- `AccountPanel.swift`: the Account section's account: signed out (why, «Войти», «Создать учётную запись», the problem);
  signed in, in this order: who (`PlayerAvatar`), the profile (`ProfileEditor` or «Изменить профиль»),
  `accountActions` («Выйти», «Удалить аккаунт и данные онлайн» with its alert, E-settings-4), the plan (a tester's Pro | no Pro) and saving online (`CloudSync.onKey`). An action of
  the account itself joins `accountActions`. `PlayerAvatar` is also the
  library's.
- `ProfileController.swift`: the profile being edited, from the account's profile: the name (cut at `longestName`, none
  when only spaces), the photo (`choosePhoto`: its middle square; the latest choice or removal wins, E-settings-3) and
  `save` (one at a time, E-settings-2; true once kept, else `message`). `ProfileEditor.swift` shows it; the photo
  picker's selection is cleared after each pick, so the same photo picked again is read again.
- `Environments.swift` (`EnvironmentPanel`, Main | Test), `About.swift` (`AboutPanel`, `BuildInfo`), `TestLibrary.swift`:
  the sheet's other panels of this folder.

## The contract others may rely on

- `Account` is the one owner of the sign-in, the plan, the profile and the environment; the panels read it and call
  its methods, and keep only what they show (`editing`, the profile's draft).
- The words the sheet alone names are Workspace's pair; the panels' are this folder's (`tests/shared/i18n.test.mjs`).

## The state it owns

The sign-in and its name (MSAL, `defaults`), a tester's plan (`ml-test-plan`), the environment, saving online
(`account.saveOnline`), the sync records, bases and the playlists' record in Application Support (written only by `CloudSync` and its `PlaylistsSync`; the library's
Delete marks one through `CloudSync.markDeletedHere`, which a pass under way keeps, E-sync-1). The profile's draft lives only while the editor is open.

## Baseline entries that protect it

UX baseline: `SET-01` (Settings), `SCAN-02` (the engine's choice in Account). Quality ratchet: the folder edges of
`Account.swift`, `CloudSync.swift`, `OnlineProjects.swift`, `TestLibrary.swift` (none of the Settings files since v1.6
i-14).
