# UI edge cases decided by pattern

**Status (2026-11-28, v1.7 e-1):** current. New in v1.5 i-7; replaces nothing.

The register of the iPad app's edge cases that no spec, approved mockup or `docs/ux-baseline.md` entry defines and a
developer decided by a common pattern (`governance/roles/developer.md`, step 6; Andrei, 2026-11-26): a tap that misses
or lands beside a target, a tap outside an open menu, a cancelled or interrupted gesture, a repeated tap, an empty or
extreme selection. Agents keep it; specs and mockups are never edited to record such a decision.

- **What it is not.** Not a spec and not approved behaviour: a spec, an approved mockup or a baseline entry always wins
  over an entry here, and an entry that contradicts one is removed. Andrei sees new entries in the version note
  («Подпилено без вас») and can overturn one; one he wants kept for good becomes a `docs/ux-baseline.md` entry through
  the baseline's own process, and its line here says so.
- **Who reads it.** A developer, before deciding a case of its own: the same case already decided here is followed,
  so the app answers it the same way everywhere.
- **An entry.** One line per case, under its area: the id (`E-<area>-<n>`), the case, the decision, the source of the
  pattern (another place in the app, Apple's Human Interface Guidelines, the usual behaviour of score or document apps),
  the pull request. The code that implements it carries a one-line comment naming the id.

## Score and selection

- `E-score-1` · Bars taken from another version or restored (performance.take, .takeAll, .restore) or a version with
  bars taken from it removed, while a passage is selected · the selection keeps its passage over the new notes and a
  variation in progress, made from the old notes, goes; when the notes stay the same (bars restored that were never
  taken, a double tap) nothing changes · the same as after a correction (`SelectionSession.FollowNotes`, «Править ноты»)
  · v1.5 w17-4
- `E-score-2` · «Пюпитр»'s strips (spec score-reading §3) on a draft's view, whose pill always shows · the draft's pill
  is the top strip's second row, the strip a row taller, so nothing lies on the page · a toolbar's second row under the
  navigation bar (Apple HIG, toolbars) · v1.7 e-1
- `E-score-3` · What hung under the floating bar: the Pen's palette and Select's row (`ModeOptions`), the recording heard
  and the read-only line · they hang under the top strip over the page as before: the player opened the first two (spec
  §3.2, «остаётся поверх листа»), the line takes no touch · the bar's own behaviour since #511 4.2 · v1.7 e-1
- `E-score-4` · A drawer's or a sheet's height on the Notes screen · a share of the room between the strips, never of the
  screen, so neither reaches a strip; the roll drawer's kept share keeps its number · the spec's «между полосами»
  (§3.2) · v1.7 e-1
- `E-score-5` · A hidden keyboard's bar (a hardware keyboard's language and Undo) over the bottom shelf while a field is
  focused · the shelf rises above it for that while, over the foot of the page, never above a whole keyboard · the
  pill's rule since v0.6.4 (`KeyboardLift(barOnly:)`) · v1.7 e-1
- `E-score-6` · Large Dynamic Type sizes and narrow Split View widths · the strips keep their height (the page never
  shrinks or jumps); the bar and the shelf take their compact forms (the shelf's «⋯», then its tight form) · fixed-height
  toolbars that collapse items into More (Apple HIG, toolbars) · v1.7 e-1
- `E-score-7` · The chip back to the playing («К месту игры · такт N»), which comes by itself while the page is
  scrolled away from the music playing · it is the bottom shelf's, beside Play in every room, tight too (the one target
  that matters while playing; with little room the other items go into «⋯» first), never over the page (spec §3.2,
  «ничего не лежит на листе»); the selection's toasts stay between the strips above the shelf, as a toast the player
  caused · a toolbar item that shows while it applies (Apple HIG, toolbars); the lead's decisions in review 1 · v1.7 e-1;
  no walk test (a known gap)

## Roll

## Menus, sheets and popovers

- `E-sheet-1` · Undo on the toast of «Сделать рабочими» in the Sources sheet · writes the project file back as it was
  just before, rather than making the earlier notes working again: the bookmarks and phrases that followed their bars
  (`FollowedWork`) may not map back exactly (a place in a bar with no match, a bar read shorter) · as the sheet's
  removal toasts undo (`SourcesController.remove`); nothing is lost without undo · v1.5 c-11b
- `E-sources-1` · Send («Отправить …») on an original's «Распознать ноты» while another original of the piece is being recognised ·
  the panel says which one is («Сейчас распознаётся «Henle.pdf»…») instead of Send, and a Send that still comes sends
  nothing; once it is done, the panel offers Send again · as the library recognises one piece at a time (Andrei,
  2026-11-25) and the panel already waits for it; before, both went to the server, and the first, coming back, cleared
  the second's progress and gave its Send back (`SourcesController.reading` holds one original). The time line before
  Send (E-scan-5) is asked again when what blocks the panel changes (another recognition, the library's), so it comes
  back once that ends · v1.6 i-13; since c-14b3 Check's «Распознать заново с отметками» holds the same `reading` for its
  PDF (`setReading`): while it reads, Sources' Send of that PDF shows its progress and others wait, and while Sources
  recognises, the button in Check is off and says which original is being recognised; no second job, no quota charged
  twice
- `E-sources-2` · A file picked in Files from the Sources sheet (a PDF, a score's file, the same file as a deleted
  original) · read as a file picked for «Привязать оригинал» is (`LinkedFile.read`: coordinated, off the main thread, so
  a file only in iCloud is downloaded first), and when it cannot be read the sheet's danger line says why in the same
  words («Файл не прочитался.», no connection, the file gone) · as `OriginalsController` does (lead's decision); before,
  it was read on the main thread, a file only in iCloud failed, and nothing was said · v1.6 i-13
- `E-settings-1` · Two settings changed in a row in the Settings sheet (or a switch turned off and on again) before
  music-core has answered the first · each change waits for the one before it and starts from the values it kept, so
  every change is kept and a switch ends as last set; before, the second answer dropped the first change, and a switch
  turned off and on at once stayed off · a control shows what was last chosen (HIG); as the score's calls go one after
  another (`DeviceSettings.queueChange`) · v1.6 i-14
- `E-settings-2` · «Сохранить» of the profile tapped again while the first save is on its way (before the button
  shows as off) · one save; the second tap does nothing · as Keep tapped twice keeps one idea (`E-insights-3`) and the
  button is off while it saves (`ProfileController.save`) · v1.6 i-14
- `E-settings-3` · The profile's photo chosen again, or removed, while one picked before still loads; the same photo
  picked again after «Убрать фото» · the latest choice shows: a photo that loads after a later choice or a removal is
  left, and a photo picked again is read again; before, the slower one won, and the same photo picked again did nothing
  (the picker kept it selected) · a control shows what was last chosen (HIG), as the plan's latest ask wins
  (`Account.refreshPlan`) · v1.6 i-14
- `E-settings-4` · «Удалить аккаунт и данные онлайн» (Settings · Account, the 1.6 plan's decision 14) · asks once more
  in an alert whose destructive button says «Удалить» and whose text says what goes (the pieces online and the profile),
  what stays (the pieces on this iPad) and that a device still signed in saves its changed pieces online again; while it
  runs the account section is busy and takes no tap; a failure keeps the player signed in and says why in the danger
  line; once done the player is signed out and the signed-out section says the data online went. Where the app may not
  write online (Main in a version build) the button is off and a line says why · HIG: confirm a destructive action that
  cannot be undone; as «Очистить всё» (`TestLibraryPanel`); a control that cannot act is disabled with why · v1.6 c-17b
- `E-settings-5` · The pieces on this iPad after «Удалить аккаунт и данные онлайн» · they stay with their sync records:
  signed in again, each counts as deleted online, as when another device deleted it, and goes online again only once it
  changes here; nothing is sent back by itself to an account the player has just emptied · as a piece deleted online
  elsewhere (`ProjectSync`, "forget") · v1.6 c-17b. Known limit: a save already on its way when the delete is sent
  (a sync pass under way in the library) may land after it; the player then sees that piece online again
- `E-sync-1` · A piece deleted in the library while a sync pass runs · its mark (`SyncRecord.deletedHere`) survives the
  pass writing the records it read before, also on a piece that pass sent for the first time, and the next pass deletes
  it online · robustness rule: nothing the player did is lost by a race (`CloudSync.run`) · v1.6 c-17b. Known limit: a
  mark written in the instant between the pass reading the records again and writing them (no wait between the two) is
  lost; the piece then stays online and is offered under «Нет на этом iPad» to delete again
- `E-sync-2` · A piece deleted here whose online copy is still to go (offline, signed out, Main read-only, the pass not
  run yet) · it is not offered under «Нет на этом iPad»; the next pass that can write deletes it online; one saved on
  another device since is fetched again instead (the 1.6 plan's decision 26) · the player deleted it: a list never offers
  back what was just removed · v1.6 c-17b
- `E-sync-3` · A merge whose previous copy or merged file cannot be written (the disk full) · nothing is kept and nothing is
  sent: the previous copy is written first, and goes again when the merged file then cannot be written; the conflict line
  is told only once both are written; both copies stay as they were and the next pass merges again · robustness rule:
  nothing is lost, nothing half written stays (`CloudSync.keep`) · v1.6 c-17b
- `E-sync-4` · The lines of pieces a pass resolved with a conflict («„…“ изменили на двух устройствах по-разному (идеи,
  пометки). Версия, сохранённая раньше, лежит рядом: „…“.») · under the library's header until «Скрыть», a later pass
  with conflicts of its own or another environment; a pass with none leaves them · as the line of pieces over the free
  room stays (`online.overQuota`); a status line the player may have missed is not taken away by a pass in the
  background · v1.6 c-17b
- `E-sync-5` · A later build's copy on this iPad (written by a resolve, or put in the folder) · kept here read-only and
  never sent: the pass does not ask music-core to send it again every time (`project.online` refuses it) and says
  nothing. One online that meets this iPad's changed copy stays the piece, read-only, and this iPad's goes beside as a new
  piece, sent as new (the lead's decision 39, amending 36: writing over it would remove what the later build wrote,
  `ProjectSync.Apart`); one here goes beside and the online copy takes the piece (decision 36) · robustness rule: no
  repeated error for what the player cannot change · v1.6 c-17b
- `E-sync-6` · A merged file whose upload fails (no connection, the server asleep) · it stays here changed, and the next
  pass sends it over the copy it fetched (`If-Match` that copy's ETag); saved elsewhere meanwhile (412), the next pass
  merges again · robustness rule: nothing is lost, nothing is written over without its ETag · v1.6 c-17b
- `E-sync-7` · Two copies the merge cannot merge (it fails its self-check, 0026 C5: Notes removed on one, renamed on the
  other) · the copy saved last stays the piece with the other's Notes, the other goes beside as a piece of its own, and
  the piece goes online with the next pass over the copy fetched (`If-Match` its ETag), so the next download cannot
  write over what only this iPad has · as `ProjectSync.Resolve` asks of an app that sends "file" · v1.6 c-17b

- `E-sheets-1` · A recording with note lengths to check (#352) and no flag open: none at all, or every one resolved · More
  offers Check (as for flags); the drawer's heading is «Длительности под вопросом», not «Все такты проверены» (false over
  marks still shown), and the lengths' own heading is not repeated under it; with no flag at all the line on flagged bars
  is left out too (only where the notes were heard). A mark goes when its note is fixed (any correction makes it the
  player's), or with «Верно» on its bar's flags; a bar with no flag has no «Верно», its entry only «Показать» (lead,
  2026-11-26). With no mark left, «Все такты проверены». · Pattern: a list's heading names what it holds; a text never
  states what did not happen (robustness rule); Check is offered wherever it holds something. · v1.5 c-15b.

## Pen and marks

## Library, import and scan

- `E-scan-1` · A page of the PDF no engine read is marked at a bar that has a Check flag already · one flag: the page's
  words are added to that flag's label, and a tap opens Check, which lists both · as a bar's notes sit beside its flag,
  never on it (`page-marks.ts`); HIG: no control hidden under another · v1.5 i-7
- `E-scan-2` · The unread page is the PDF's last (after every page the notes have) · its mark stands at the notes' last
  bar and says «её место после такта N»; «Показать» selects that bar · the same mark as before a bar, the one place left
  · v1.5 i-7
- `E-scan-3` · «Повторить» in Check while the piece is open on its notes · the page is sent at once, but put into the
  notes only once the piece is closed; meanwhile its mark leaves the score and Check says «Стр. N читается заново: она
  встанет в ноты, когда пьеса закроется»; a second tap is not offered · as the open PDF's background recognition, whose
  notes wait for its screen (SCAN-04); a running action shows it is busy · v1.5 i-7
- `E-scan-4` · The batch's consent while the scan server is asked how long (a hosted server may take up to two minutes
  to wake) · «Сколько ждать» says «Спрашиваю сервер распознавания, сколько это займёт…» and «Распознать ноты» stays
  usable; no answer, or a server without jobs, gives the line it gave before (half a minute a page) · a missing loading
  state is shown, never a number not known yet (robustness rule) · v1.5 i-7
- `E-scan-5` · Sources' recognise panel for a recording, or for pages while something blocks them (sign-in, Pro, a
  recognition already going) · no estimate line: only pages sent as a job have one, as the PDF's own panel · v1.5 i-7
- `E-scan-6` · A page read again comes without its report into notes that keep each page's report · the page goes in and
  the notes keep no reports from then on (one per page or none), so Check no longer tells a page's print quality; never
  a refusal in words the player cannot act on · robustness rule (a page read is not lost for a missing number) · v1.5
  i-7 (review 1)

- `E-scan-7` · A page read again («Повторить») whose place a motif or an idea's variation of the notes spans (it starts
  before the page's place and ends after it), or whose notes a motif, variation, highlight or finger names but the page
  changes · the page is not put in and the line keeps «Повторить» with why («Не вставлено: мотив, вариация идеи или
  пометка на ноте перестали бы относиться к тем же нотам»); the rest of the work, also a bookmark or margin note across the
  place, moves on by the page · as a correction across the place refuses the page, nothing dropped (robustness rule); a
  passage of notes is its notes, so it cannot take the page's in as a bookmark's bars do · v1.5 r-2b
- `E-scan-8` · A piece deleted from the library while the batch reads it, its file's name perhaps taken meanwhile by a
  new import of the same PDF · its notes are not written back (the piece stays deleted, the new one as it is: the file
  must still be the project read at the start) and its line says «Пьесы больше нет в библиотеке», its job deleted on the
  server as one that did not work · as a piece missing when its turn comes (`recognition.failMissing`); a deletion is not
  undone by a background job · v1.5 r-2b
- `E-scan-9` · An engine's reading of a bar tapped again before music-core has put the first tap's in · the readings'
  buttons are disabled until it is in, and a tap that comes before they show it does nothing, so the bar holds one
  reading, which «Отменить выбор» takes back whole · a button's action runs once per press while it is under way and
  shows that it waits (HIG: prevent a repeated submit); SCAN-06: one tap puts one reading in · v1.6 i-8
- `E-library-1` · A very long title on a Sessions card · two lines at most, cut with an ellipsis; VoiceOver reads it
  whole · as the list's line, which takes one; HIG: truncate rather than let one card grow the grid · v1.5 r-2b
- `E-library-2` · «Открыть файл» on a PDF this iPad cannot show (broken, no page, locked with a password) · refused with
  «Этот PDF не удалось прочитать…» over the library and no piece made · as a damaged MusicXML is refused, and as a folder
  import finds such a PDF unreadable (`FolderScan.facts`) · v1.5 r-2b
- `E-library-3` · A new piece's project file whose title is long, starts with a dot or holds characters a file name
  cannot · named by the title with those characters as spaces, no dot first (a hidden file is never listed), cut between
  letters to at most 200 bytes, numbered when the name is taken (a title the player or the score gives is shown as it
  is; one only the file's name gives shows that name) · the Files app's rules for a name · v1.5 r-2b
- `E-library-4` · A piece asked to open while another is being opened (a line, Add piece, Files, the example, «Скачать
  сюда», a screen's PDF | Notes, a folder's piece) · the second ask does nothing; the library waits for the first, whose
  step always ends with the library answering again, a downloaded copy that cannot be read saying why · as PDF | Notes
  already did (#582 item 2), one opening owner (`LibraryModel.openOnce`); HIG: prevent a repeated submit · v1.6 i-11b
- `E-library-5` · «Удалить» on a piece whose project file cannot be removed (no permission, the file busy) · its line
  stays and the line over the library says why (««…» не удалось удалить с этого iPad: пьеса осталась в библиотеке.»,
  `library.deleteFailed`); a file already gone (deleted in Files meanwhile) is taken off as deleted · the list states
  only what is on this iPad, as the next reading would show it · v1.6 i-11b. Known follow-up for the robustness pass: a
  file that cannot be read on open, in Files or by rename still says the system's own words (`localizedDescription`)
- `E-pdf-1` · «Разделить на пьесы» pressed again while the collection's project file is saved, before its first piece
  is made · the split is under way from the press: «Разделяю: 0 из N» shows and the button is off until it ends, so the
  collection is split once (a press then made every piece twice) · a button's action runs once per press while it is
  under way and shows that it waits (HIG: prevent a repeated submit), as `E-scan-9` · v1.6 i-11. Also while «Предложить»
  or «Подтвердить пьесы» is under way (it saves the piece's file): «Разделить на пьесы» is off and a press is refused,
  as those two are off while a split is under way · v1.6 i-11 (review)
- `E-scan-10` · The unread pages of a scan whose queue item is gone (the app restarted, the job finished elsewhere) · the
  pages kept in the project file (`check` answers `unreadPages`, `readPages`) are marked and listed in Check as before,
  without «Повторить», which needs the queue's PDF · robustness rule: a page no engine read is never forgotten silently
  · v1.6 c-14a
- `E-library-6` · A project saved by a newer version of Motiff Lab that this build may read · it opens and plays; one line
  hung under the bar of every view of the piece (PDF, Notes, the roll, a recording) says it is read-only and nothing done
  is saved (Russian and English), written once by `PieceScreen` and drawn by `TopBar`, never one copy per screen; it takes
  no touch and does not move the bar; the work done is not saved, and no save is offered as failed · robustness rule: the
  app states what did not happen, the newer file is never written over · v1.6 c-14a
- `E-scan-11` · A PDF read from its own panel's «Распознать ноты» rather than by the library's recognition, some pages
  read by no engine · the pages go with the notes the same way (`DocumentRead.request` of the `ScanResult`, one rule for
  both flows), so the score marks them and Check lists them after the app restarts · robustness rule: a page no engine
  read is never forgotten silently · v1.6 c-14a
- `E-scan-12` · A mark «что не так» on a bar that has a Check flag already, or a mark over several bars · one flag at its
  first bar: on a flagged bar its words join that flag's label, and a tap opens Check, which lists both · as E-scan-1;
  HIG: no control hidden under another · v1.6 c-14b3
- `E-scan-13` · «Размер» or «Тональность» chosen from the selection menu over several bars · the mark is put at the
  selection's first bar, from where it holds until the next of its kind · music-core keeps a meter or key from its bar on;
  a time or key signature is printed once at the bar where it changes · v1.6 c-14b3
- `E-scan-14` · A clef or an octave sign on notes with one staff, or on more than two · the menu always offers the upper and
  the lower staff · a piano score's two staves, as the marks' words say them (`верхний стан`, `нижний стан`) · v1.6 c-14b3
- `E-scan-15` · A mark made from the selection menu, where nothing else shows that it was kept · a word over the pill says
  it is marked and that Check reads the marked pages again; from Check, whose list shows it at once, nothing more · as a
  bookmark saved from the menu is told; HIG: confirm an action whose result is not in view · v1.6 c-14b3
- `E-scan-16` · Notes not read from a PDF or photos, or notes only shown over the piece · no «Что не так?» in Check or the
  selection menu (music-core refuses the marks there); on notes only shown, Check lists the marks without «Убрать» and
  without «Распознать заново с отметками» · a control is offered only where it works (robustness rule) · v1.6 c-14b3
- `E-scan-17` · «Распознать заново с отметками» tapped again while the marked pages are read, while the library
  recognises a piece, or while Sources recognises an original (E-sources-1) · the button is disabled and the progress,
  «Идёт распознавание «…»» or «Сейчас распознаётся «…»» shows; a tap that comes before it does nothing · as E-scan-9; one
  recognition at a time (Andrei, 2026-11-25) · v1.6 c-14b3
- `E-scan-18` · A marked page no engine reads again · the whole reading fails with «Ни один движок не прочитал заново
  страницы: N»; no new Notes, the working notes as they were, the marks kept · a page kept with its old reading beside
  pages read again would take the marks twice or not at all (hands, octave); nothing half done (robustness rule) · v1.6
  c-14b3
- `E-scan-19` · Which marked pages leave the iPad, and what goes · a page the vector reader reads stays on the iPad (free,
  no server), unless a bar on it is marked as read badly, which only the server's other engines read otherwise; when a
  page has to go, the whole PDF goes, as a scan job of Sources' «Распознать» does, to the scan server set in Settings,
  with only the marked pages to be read; before the press Check says how many pages are read on the iPad and where the
  others go, in Sources' words («2 страницы уйдут на сервер распознавания Motiff Lab…» or «… на ваш сервер распознавания
  http://…»), Pro only then on Motiff Lab's; that needs a server that takes jobs («Четыре движка») and, on Motiff Lab's,
  sign-in and Pro, else Check says why and nothing is sent; pages with no mark are not read again and keep their reading.
  When the notes name no pages read and the PDF has another number of pages than they have, nothing is read («У этих нот
  и PDF … разное число страниц…»), so a second reading never reads the wrong page; with the PDF not kept in the piece the
  button says so and is off · ADR 0011 section 2: where the pages go is named before they go; on the device = free,
  cloud = Pro (0025); only what the player asked is read; a control is offered only where it works · v1.6 c-14b3
- `E-scan-20` · The screen left while the marked pages are read · the reading stops, its job is deleted on the server, and
  nothing changes in the piece · as the PDF's own panel closed mid-reading (`ScanProblem.stopped`); a job no screen
  waits for is not left on the server · v1.6 c-14b3
- `E-scan-21` · What the new Notes of a reading with marks keep · the readings and crops of the bars to check of the pages
  read again, under the new notes' fingerprint; no page reports, as only some pages were read, so Check does not tell a
  page's print quality for them; a bar read again (its flag «Прочитан другим движком») shows its crop and readings when
  selected, as a bar the engines disagree on, so the earlier reading can be taken back · as E-scan-6 (one report per page
  or none) · v1.6 c-14b3

## Insights

- `E-insights-1` · A Try, «Оставить» or «На круге» that music-core refuses while the facts are shown · its words show
  over the facts, in the stream's danger line, until the next step (another way, Keep, Leave, the circle opened,
  another selection) · the stream already said a refusal so when nothing was understood; a fallible step shows its error state
  (developer's role, step 6) · v1.6 i-12
- `E-insights-2` · A phrase tapped (or the selection cleared), with the sheet open or closed, Leave, Sources opened or
  the screen handed over while a way is being made · the way is left: music-core
  forgets the variation, nothing sounds, no trial and no refusal shows on the new selection's facts · a variation
  belongs to the passage it was tried on, as Insights leaves a trial when the selection changes; a stale answer is never
  shown (as `E-drafts-3`) · v1.6 i-12
- `E-insights-3` · «Оставить» tapped again before music-core has kept the idea · one idea; the second tap does nothing
  · one step at a time, as a second Try while a way is made (`starting`) and `E-drafts-4`; HIG: prevent a repeated
  submit · v1.6 i-12
- `E-insights-4` · «Привязать заново» tapped again before music-core has answered · one reattach and one toast; the
  second tap does nothing · as `E-insights-3`; HIG: prevent a repeated submit · v1.6 i-18
- `E-insights-5` · Insights on a view only shown (a draft's view, other Notes over the piece) whose anchors match no
  phrase · no line «Границы фраз изменились» and no «Привязать заново»: the phrases of such a view cannot be changed ·
  a control is not offered where its action cannot work (developer's role, step 6), as the phrase marks are not drawn
  there (PHR-01) · v1.6 i-18
- `E-insights-6` · An idea attached to no phrase now: kept on a passage that overlapped none, or whose anchor matches no
  phrase since the notes or their analysis changed · it shows in the stream by the bars it was kept on («такты 5–8»),
  with any selection that overlaps them, until «Привязать заново» attaches it again · nothing kept is hidden; the bars
  are the one place it still has · v1.6 i-18
- `E-insights-7` · «Привязать заново» where no phrase is left at the anchors' places (the piece became shorter) ·
  nothing is attached, the toast says «Привязать не к чему: на этих местах фраз больше нет» and the line stays · a
  step says what it did, also when it did nothing (the phrases' toast) · v1.6 i-18
- `E-insights-8` · The stream read for another selection (the sheet reopened on it, or the selection moved with the
  sheet open) · the facts of the passage before go at once and the stream shows it reads from that first step (a trial
  being left too), until the new passage's facts come; a second load for it meanwhile does nothing · a stale answer is never shown (as `E-insights-2`, `E-drafts-3`): the old facts' Try and places would act on the
  new selection · v1.6 r-3
- `E-dna-1` · Piece DNA asked again (More closes at once) while music-core still works out the map of a long piece · the
  second ask does nothing; the map opens once · as `E-library-4`; HIG: prevent a repeated submit · v1.6 r-3

## Drafts and the assistant

- **E-drafts-1** · The card «Новый черновик» for a passage selected in notes heard in a recording: that recording is
  not searched for the passage (it is where the passage is), others are; with no other, the card says nothing of a
  recording and offers no «С ассистентом · Pro», never «На записях нет этой музыки.». Pattern: «Найти на записи»
  never offers a place the stretch it is made of («Уже вариант этого места»). v1.5 d-11.
- **E-drafts-2** · On a view shown over the piece (other Notes, a version), which has no switch to show a draft's view,
  the card «Новый черновик» still says where the passage is on the recordings but offers no «С ассистентом · Pro»;
  «Создать» as before. Pattern: a view shown over the piece offers no switch nor Sources (0030). v1.5 d-11.
- **E-drafts-3** · The «В черновик» sheet closed, or another part chosen, while its card searches the recordings: that
  search's result is dropped, and the card searches again for the part chosen; «Создать» does not wait for the search.
  Pattern: «Найти на записи» on a place's card (DRAFT-13), a stale answer never shown. v1.5 d-11.
- **E-drafts-4** · A second tap of «Создать», «Добавить» or «С ассистентом · Pro» while the first is on its way does
  nothing: one draft or fragment, not told as refused. Pattern: one step of a draft at a time (`stepping`, v1.4). v1.5 d-11.
- **E-assistant-1** · Another conversation, or a new one, opened while a question is on its way: the question shows only
  under the conversation it was asked in (a new one's too, kept with its answer), and its answer joins that one without
  opening it again. Pattern: nothing pulls the player back (ChatFollow); a stale answer never shown elsewhere. v1.6 r-3.
