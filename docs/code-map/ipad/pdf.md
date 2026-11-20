# Code map: iPad `Pdf/`

Index: [`../ipad.md`](../ipad.md). Folder: `src/ipad/App/MotiffLab/Pdf`.

## Files

`Pdf`, `PdfContents`, `PdfNotes`, `PdfPagesView`, `PieceParts`, `PiecePartsSheet` (`.swift`). The pen on the PDF's pages is
`Ink/PdfInk.swift`. Switching PDF | Notes: [`library.md`](library.md).

- `Pdf.swift`: `PdfScreen`. With notes, the switch of all views and Sources in the bar; a PDF of the piece's own with no
  notes yet (`withoutNotes`, v1.1, finding 29) has the PDF tag and Sources alone, where a recording added is recognised;
  the piece's first notes open it again as notes (`reopenHere`, `PdfScreen.reopening`), anything else at the page.
- `PdfContents.swift`: the Contents' bookmarked pages (`PdfContents`, `PdfBookmarkRow`), moved out of `Pdf.swift` in v1.1.

## The contract others may rely on

Not recorded yet.

## The state it owns

Not recorded yet.

## Baseline entries that protect it

UX baseline: `VIEW-01`, `VIEW-02` (switching PDF | Notes). Quality ratchet: `Pdf.swift` file_length.
