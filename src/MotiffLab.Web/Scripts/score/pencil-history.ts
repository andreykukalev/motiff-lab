// The pen's own history (#533 section 9): the steps Undo takes back and the steps Redo does again, on the score and on a
// PDF's pages alike (pencil-layer.ts), as the iPad's InkHistory keeps them (ipad/App/MotiffLab/InkGeometry.swift). A step is
// the marks it added and the marks it took away: a stroke, a press of the eraser, or Clear. A new step drops what Redo had.

/** One step the player can take back: marks it added and marks it took away. */
export interface MarkChange<Mark extends { id: string }> {
    added: Mark[];
    removed: Mark[];
}

/** The steps kept for Undo. */
const UNDO_DEPTH = 100;

export class MarkHistory<Mark extends { id: string }> {
    private done: MarkChange<Mark>[] = [];
    private undone: MarkChange<Mark>[] = [];

    get canUndo(): boolean {
        return this.done.length > 0;
    }

    get canRedo(): boolean {
        return this.undone.length > 0;
    }

    /** A new step: it can be taken back, and what Redo had goes. */
    record(change: MarkChange<Mark>): void {
        this.undone = [];
        this.done.push(change);
        if (this.done.length > UNDO_DEPTH) {
            this.done.shift();
        }
    }

    /** A new piece, its marks changed from outside, or shown only: nothing to take back or do again. */
    forget(): void {
        this.done = [];
        this.undone = [];
    }

    /** The marks with the last step taken back, or null when there is none; Redo can do it again. */
    undo(marks: readonly Mark[]): Mark[] | null {
        const change = this.done.pop();
        if (change === undefined) {
            return null;
        }
        this.undone.push(change);
        return replace(marks, change.added, change.removed);
    }

    /** The marks with the step taken back last done again, or null when there is none. */
    redo(marks: readonly Mark[]): Mark[] | null {
        const change = this.undone.pop();
        if (change === undefined) {
            return null;
        }
        this.done.push(change);
        return replace(marks, change.removed, change.added);
    }
}

/** The marks without those `gone` names, with `back` after them. */
function replace<Mark extends { id: string }>(marks: readonly Mark[], gone: readonly Mark[], back: readonly Mark[]): Mark[] {
    const ids = new Set(gone.map((mark) => mark.id));
    return [...marks.filter((mark) => !ids.has(mark.id)), ...back];
}
