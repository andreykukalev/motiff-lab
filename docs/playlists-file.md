# The playlists file, version 1

**Status (2026-11-28, version 1.7, item p-2):** music-core reads, writes, edits, moves and merges it (`MotiffLab.MusicCore.Projects.Playlists`, the facade's `playlists.*` calls). The iPad keeps it and switches the library to it in p-4; the API keeps one per player and the sync sends it in p-3. Andrei's decision 3 of the 1.7 plan (format 19, «2 да»); the lead's decision 3: a playlist has one owner, this file, and its pieces are not kept in the pieces as well.

A playlist is a record of the library of its own (the score-reading spec, section 6): a name the player gives and the pieces they put on it, in their order. Before 1.7 a playlist was only a name in each piece's `metadata.playlists` ([project-file.md](project-file.md), "Title, composer, tags and playlists") and existed while a piece was on it. The file sits beside the project files on the device and online as one document per player; it never holds a piece itself, only the pieces' project ids.

## The document

```jsonc
{
  "format": "motiff-lab-playlists",
  "version": 1,
  "minReader": 1,                       // the oldest version that may read the file
  "playlists": [
    {
      "id": "list_…",                   // "list_" and 32 hex digits
      "name": "Бах",                    // one line, unique in the file whatever the letter case
      "pieces": [ "p_…", "p_…" ]        // project ids, each once, in the player's order
    }
  ]
}
```

`PlaylistsFile` writes it by hand, as `ProjectFile` writes a project, so the same playlists give the same bytes (indented, texts outside ASCII escaped). The playlists stand in the order they were made; the library shows them in its own order.

## Reading

- **Not a playlists file** (not JSON, not an object, another `format`) is refused. `version` and `minReader` are whole numbers of 1 or more, `playlists` an array, each playlist an object with a text `id` (not empty, no two alike), a text `name` and `pieces`, an array of texts; anything else is damage, refused with what cannot be read.
- **By the rules:** a name is read as one line (runs of white space one space, ends trimmed: `PieceMetadata.Clean`, as a tag's or a piece's playlist's) and an empty one is damage; a piece twice on a playlist is there once, at its first place; two playlists of one name whatever the letter case are one, the first, the later one's pieces after its own (`LibraryPlaylists.Joined`). A file another writer made reads so; written again, it is the file by the rules.
- **Unknown fields** at the top level and in a playlist are kept and written back after the known ones, in the order they came (0026 S6).
- **A later version:** a file whose `minReader` is above 1 is refused ("These playlists were saved by a newer version of Motiff Lab", in Russian «Эти плейлисты сохранены более новой версией Motiff Lab»). One of a later `version` whose `minReader` is 1 is read as this layout, every unknown field kept, and is **read-only** (`LibraryPlaylists.Newer`): shown, written back with its own `version` and `minReader`, never saved (`PlaylistsFile.Save` refuses it, and so does every edit call, `readOnly`).

## The rules

The edits are pure (`LibraryPlaylists`), each answering the playlists it makes:

- **Make** a playlist with a name, empty or with pieces: it goes at the end with a new random id (the facade's `IdSource`, so two devices never make one id) and stays while it has no piece. A name another playlist has, whatever the letter case and the white space, makes nothing and answers that playlist (`taken`), so the app offers to open it (spec 6.5). An empty name is refused.
- **Rename:** the same rule; the playlist may change only the letter case of its own name.
- **Delete:** the playlist goes; its pieces stay in the library and on the other playlists.
- **Add pieces:** at the end, in the order given, each once; one already on the playlist stays where it is. A piece may be on several playlists.
- **Remove pieces** from one playlist; they stay in the library and on the others.
- **Move pieces** (a drag of one row or several): the pieces named go, in the order given, before the piece named, or to the end; a piece not on the playlist is passed by; moving before a piece that is not on it or is moved itself is refused.
- **Forget pieces:** a piece the library no longer has (deleted here, or on another device) goes from every playlist.
- **Re-key:** a piece kept under an id it had before (`formerIds`: a numbered id re-keyed, a copy merged under the online id, #317) is put under its id now, once, at its first place, so it stays on its playlists.

«Держать на этом iPad» (version 0.5) is not in the file: it is this iPad's choice, kept by the app on the device by the playlist's name (`KeptCopies`), never online.

## Moving the pieces' playlists here

Once, when there is no playlists file yet (`playlists.migrate`), the playlists are made from the pieces' `metadata.playlists`, losing nothing: the app sends every piece, in the order the library lists them, with the names it holds. A playlist comes in the order its name is first met, spelled as first met; its pieces stand in the order sent, which is the order the library showed a playlist's pieces in until 1.7 (from A by title, as the list is ordered by default), so nothing moves. Names are cleaned and taken whatever the letter case, as the library took them. The id of a playlist moved is of its name: `list_` and 32 hex digits of the SHA-256 of `playlist|` and the name in lower case (`IdSource.Hashed`), so two devices that move the same pieces make the same playlists, and the merge takes them as one.

Until p-4 switches the library to this file, the pieces still write `metadata.playlists` ([project-file.md](project-file.md), version 19); p-4 stops writing it in the same pull request as the switch (the lead's decision 11).

## Sync

Two copies (this device's and the online one) merge three-way against the copy both last agreed on (`PlaylistsMerge`, `playlists.resolve`), by the principles of a project's merge ([project-file.md](project-file.md), "Sync"; 0026 S4, S5, C2):

- **By playlist id.** A playlist changed on one side only takes that side; added on either side, it comes in; deleted on one side and unchanged on the other, it goes, and so it does when the other side only took pieces off it (the player removed them, or a deleted piece was forgotten); deleted on one side and renamed, given pieces or reordered on the other, it stays with the change, so nothing the player added is lost (the lead's decision, 2026-11-28).
- **The name** is a value as a whole: renamed on both differently, the copy saved last keeps its name and the merge records a conflict (`{"in": "playlists.name", "id": "list_…"}`).
- **The pieces** are a set: a piece added on either side comes in, one taken off on either side goes.
- **The order** is the copy saved last's, unless only the other copy reordered the pieces both still have since the base: then the other's. The pieces only the other copy added go at the end, in its order, as a piece added does. Both reordered differently: the copy saved last's order, and a conflict (`"in": "playlists.pieces"`).
- **Without a base** the merge is a union with no deletions (S4), the order the copy saved last's.
- **A field this build does not know** merges as a whole value (`"in"` its name at the top level, `playlists.` and its name in a playlist).
- **Two playlists that come to share a name** (made apart on two devices, or one renamed to another's name) are one after the merge, as on reading: the first keeps its id and name, the other's pieces join it at the end. Nothing is a conflict there, as nothing is lost.
- **What goes online:** the copy kept, whenever it is not the online copy (`upload`).
- **A later build's copy** is never merged nor sent (as a project's, the 1.6 plan's decision 14): the online copy is kept as it came, read-only when it is the later one, `merged` and `upload` false, and when this device's copy differs from the base (or from the online copy, without one), `{"in": "playlists", "id": null}` says its changes are not in the copy kept.

The merge keeps no conflict copy beside, as a project's does: a conflict loses only a name or an order that the copy saved last replaced.

## The facade

On piece 0 (contract 17, calls added; [facade-contract.md](facade-contract.md)). The file travels as its text in `"file"`, as a project file does; no `"file"` is no playlists yet. Each call but `playlists.resolve` answers `{"file": the file to keep, "readOnly", "playlists": [{"id", "name", "pieces"}]}`:

| Call | Request | Also answers |
| --- | --- | --- |
| `playlists` | `file`? | (the file by the rules, written as it came) |
| `playlists.create` | `file`?, `name`, `pieces`? | `id` (null when taken), `taken` (null, or the playlist with the name) |
| `playlists.rename` | `file`, `id`, `name` | `id`, `taken` |
| `playlists.delete` | `file`, `id` | |
| `playlists.add`, `playlists.remove` | `file`, `id`, `pieces` | |
| `playlists.move` | `file`, `id`, `pieces`, `before`? | |
| `playlists.forget` | `file`, `pieces` | |
| `playlists.rekey` | `file`, `pieces`: [{`id`, `formerIds`}] | |
| `playlists.migrate` | `pieces`: [{`id`, `playlists`}] in the library's order | |
| `playlists.resolve` | `here`, `savedHere`, `online`, `savedOnline` (ISO 8601), `base`? | only `file`, `upload`, `merged`, `readOnly`, `conflicts` [{`in`, `id`}] |

A playlist `id` names that is not there answers `notFound`; an empty name, a move before a piece not staying, a damaged file or a time that is not ISO 8601 `badRequest`; an edit of a later build's file `readOnly`.

## Golden files

`tests/backend/Unit/MotiffLab.MusicCore.Tests/Golden/v1-worked.playlists.json` is what today's writer makes of three playlists (two with pieces, one empty; `PlaylistsFileTests`), `v2-future.playlists.json` the same with version 2 and unknown fields at the top and in a playlist, read read-only and written back whole.
