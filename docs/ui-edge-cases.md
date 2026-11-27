# UI edge cases decided by pattern

**Status (2026-11-27, v1.6 i-12):** current. New in v1.5 i-7; replaces nothing.

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

## Roll

## Menus, sheets and popovers

- `E-sheet-1` · Undo on the toast of «Сделать рабочими» in the Sources sheet · writes the project file back as it was
  just before, rather than making the earlier notes working again: the bookmarks and phrases that followed their bars
  (`FollowedWork`) may not map back exactly (a place in a bar with no match, a bar read shorter) · as the sheet's
  removal toasts undo (`SourcesController.remove`); nothing is lost without undo · v1.5 c-11b

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
