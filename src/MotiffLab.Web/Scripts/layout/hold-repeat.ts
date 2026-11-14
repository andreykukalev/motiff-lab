// Hold to repeat (version 0.5.4, #511 section 17): the tempo control's − and + step by 1 % a tap, and while held they
// step again and again, as the UX prototype Andrei approved does. A button inside the control marked data-hold-repeat
// clicks itself again every REPEAT_MS once it was held for DELAY_MS; the click its release makes is then not one more
// step. The control keeps its presses to itself, so a held button is not also the pill's long press (long-press.ts).

/** The design kit's motion tokens (tokens.css): the slow one before repeating, the fast one between the steps. */
const DELAY_TOKEN = "t-slow";
const REPEAT_TOKEN = "t-fast";

/** A token's time in milliseconds, "320ms" or "0.32s"; null when it is not one. */
export function milliseconds(value: string): number | null {
    const match = /^([\d.]+)(ms|s)$/.exec(value.trim());
    if (match === null) {
        return null;
    }
    const amount = Number(match[1]);
    return Number.isFinite(amount) ? (match[2] === "s" ? amount * 1000 : amount) : null;
}

/** Reads a token of the document, as tokens.ts does, without its colour helpers. */
function tokenTime(name: string, root: Element): number | null {
    const view = root.ownerDocument?.defaultView;
    return view ? milliseconds(view.getComputedStyle(root.ownerDocument.documentElement).getPropertyValue(`--${name}`)) : null;
}

export interface HoldTimes {
    delay: number;
    repeat: number;
}

export class HoldRepeat {
    private held: { button: HTMLButtonElement; pointer: number; timer: number; repeated: boolean } | null = null;

    /** Whether the press that just ended repeated, so the click its release makes is not one more step. */
    private swallowClick = false;

    constructor(
        private readonly root: HTMLElement,
        private readonly times: HoldTimes,
    ) {
        root.addEventListener("pointerdown", this.onDown);
        root.addEventListener("pointerup", this.onUp);
        root.addEventListener("pointercancel", this.stop);
        root.addEventListener("pointerleave", this.onLeave, true);
        root.addEventListener("click", this.onClick, true);
    }

    dispose(): void {
        this.stop();
        this.root.removeEventListener("pointerdown", this.onDown);
        this.root.removeEventListener("pointerup", this.onUp);
        this.root.removeEventListener("pointercancel", this.stop);
        this.root.removeEventListener("pointerleave", this.onLeave, true);
        this.root.removeEventListener("click", this.onClick, true);
    }

    private readonly onDown = (event: PointerEvent): void => {
        // A press inside the control is the control's: the pill's long press does not also open its settings.
        event.stopPropagation();
        this.swallowClick = false;
        const button = event.target instanceof Element ? event.target.closest<HTMLButtonElement>("[data-hold-repeat]") : null;
        if (button === null || !this.root.contains(button) || !event.isPrimary || event.button !== 0) {
            return;
        }
        this.stop();
        const timer = window.setTimeout(this.step, this.times.delay);
        this.held = { button, pointer: event.pointerId, timer, repeated: false };
    };

    private readonly step = (): void => {
        const held = this.held;
        if (held === null || held.button.disabled) {
            this.stop();
            return;
        }
        held.repeated = true;
        held.button.click();
        held.timer = window.setTimeout(this.step, this.times.repeat);
    };

    private readonly onUp = (event: PointerEvent): void => {
        if (this.held !== null && event.pointerId === this.held.pointer) {
            this.swallowClick = this.held.repeated;
            this.stop();
        }
    };

    /** A finger or the pointer that slides off the button stops it (leaving a part of the button does not). */
    private readonly onLeave = (event: PointerEvent): void => {
        if (this.held !== null && event.target === this.held.button) {
            this.stop();
        }
    };

    private readonly stop = (): void => {
        if (this.held !== null) {
            window.clearTimeout(this.held.timer);
            this.held = null;
        }
    };

    /** The release after the steps repeated is not one more; the steps' own clicks pass while the button is held. */
    private readonly onClick = (event: MouseEvent): void => {
        // A click from the keyboard (detail 0) is always a step.
        if (this.held === null && this.swallowClick && event.detail !== 0) {
            this.swallowClick = false;
            event.preventDefault();
            event.stopPropagation();
        }
    };
}

/** Entry point for Blazor: the control's root, with the motion tokens' times (320 ms, then a step every 120 ms). */
export function attach(root: HTMLElement): HoldRepeat {
    return new HoldRepeat(root, { delay: tokenTime(DELAY_TOKEN, root) ?? 320, repeat: tokenTime(REPEAT_TOKEN, root) ?? 120 });
}
