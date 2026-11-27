# Code map: iPad `Pdf/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Pdf`; the PDF piece's screen is in `Workspace/` (it
composes the features, as the score's does). The pen on the PDF's pages is `Ink/PdfInk.swift`. Switching PDF | Notes:
[`library.md`](library.md).

## Files

- `PdfController.swift`: `PdfController`, the PDF piece open (an `ObservableObject` and the pen's `PenHolder`): the
  pages read by PDFKit, the page under the bar (kept on the device, `pdf.page.<id>`), the bookmarks and strokes as
  music-core answered the last `document` call (`load`, `change`, `toggleBookmark`, `rename`, `remove`), drawn on the
  pages (`markPages`), the PDF as added for Download and Read the notes, `keepView` (where the pages are, `ViewStates`).
  `PageBookmarkEntry`.
- `PdfPagesView.swift`: `PdfPages` (the `UIViewRepresentable` that makes the pages' view and follows the pages in view),
  `BarPdfView` (PDFKit's view under the floating bar: room above the first page, a page brought to rest below the bar,
  the place kept), `PageRest` (its rules as numbers), `BookmarkTab` (a bookmarked page's tab).
- `PdfContents.swift`: the Contents' bookmarked pages (`PdfContents`, `PdfBookmarkRow`). `PdfMore.swift`: More,
  Download the PDF. `PdfNotes.swift`: `PageMap`, `OtherView`, `Landing`, `PdfNotesSwitch` (PDF | Notes).
- `PieceParts.swift`: «Пьесы в этом файле» as values and requests (`PartRows`, `PiecesLayer`, `PartsSplit`,
  `PieceFamily`). `PieceSplitter.swift`: `PieceSplitter`, the sheet's model, made from values (the rows, the family):
  «Предложить», «Подтвердить пьесы» and «Разделить на пьесы», which answers the pieces made; `PdfPartCopy`.
  `PiecePartsSheet.swift`: the sheet's views and `PageStepper` (also a draft's place's length).

`Workspace/PdfScreen.swift` (v1.6 i-11, W2.12): `PdfScreen`, its state, `body` (the pages, the bar, the palettes, the
metronome's pill, the Sources panel, the sheets) and its life (`.task` loads, `onDisappear` and the background save).
Its parts, extensions beside it: `PdfScreenBar.swift` (the bar: Back and the Contents, the switch of views or the PDF
tag, Recognise notes, the bookmark, the metronome, the modes, the Reference, More), `PdfScreenReading.swift` (Recognise
notes: the panel, the scan server, the library's recognition in the background, `fill` puts the notes into this same
piece), `PdfScreenSources.swift` (PDF | Notes and the switch of all views, Sources, `reopenHere`, `reopening`, a new
interface language). With notes, the switch of all views and Sources are in the bar; a PDF of the piece's own with no
notes yet (`withoutNotes`, v1.1, finding 29) has Sources and, with a recording, the switch of the PDF and its recordings
only (`SourceLayers.views(showing:)`); the piece's first notes open it again as notes, anything else at the page. With
«Четыре движка», «Распознать ноты» hands the piece to the library's recognition (`Platform/BackgroundReading`).

## The contract others may rely on

- `PdfScreen(piece:switchView:openRead:linked:readOnlyNote:)`, `PdfScreen.documentView`, `PdfScreen.reopening`.
- A PDF loaded again (a page photographed again, `PhotoReadingPanel`) is shown in place of the old (`BarPdfView.show`).

## The state it owns

`PdfController`: the document, page, last page, page count, bookmarks, strokes, the pen and its history, the problem
line, the family (written by the screen's `.task` and a split made). `PieceSplitter`: the rows, the playlist, the split
in progress (`making`, set before the collection is saved, so the button is off meanwhile), what was made.

## Baseline entries that protect it

UX baseline: `VIEW-01`, `VIEW-02` (switching PDF | Notes). Quality ratchet: `PdfController.swift`'s nine Ink edges (the
pen's state on the model), `PdfNotes.swift` (`SegmentedChoice`), `PiecePartsSheet.swift` (`FieldLook`).
