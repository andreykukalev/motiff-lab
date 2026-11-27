# Code map: iPad `Account/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Account`. `Account.json` (both environments'
servers) stays at the top of `src/ipad/App/MotiffLab`, where `src/ipad/build-version.sh` sets its "Default".

## Files

`About`, `Account`, `AccountPanel`, `CloudSync`, `Environments`, `OnlineProjects`, `ProfileController`, `ProfileEditor`,
`TestLibrary` (`.swift`).

- `Account.swift`: MSAL sign-in, `apiRequest(path)` (the API with the player's token), plan and Pro features, the
  profile (`PlayerProfile`, `saveProfile`).

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
  `accountActions` («Выйти»), the plan (a tester's Pro | no Pro) and saving online (`CloudSync.onKey`). An action of
  the account itself joins `accountActions` (c-17b: «Удалить аккаунт и данные онлайн»). `PlayerAvatar` is also the
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
(`account.saveOnline`). The profile's draft lives only while the editor is open.

## Baseline entries that protect it

UX baseline: `SET-01` (Settings), `SCAN-02` (the engine's choice in Account). Quality ratchet: the folder edges of
`Account.swift`, `CloudSync.swift`, `OnlineProjects.swift`, `TestLibrary.swift` (none of the Settings files since v1.6
i-14).
