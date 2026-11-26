// A page just big enough for the piano roll (Scripts/roll/piano-roll.ts) to run in Node, for the audit's guard C6
// (docs/REFACTORING_AUDIT.md): elements that keep their attributes, styles, children and listeners; canvases whose 2D
// context writes every call and every property set down as a line; a clock and animation frames the test advances; the
// design kit's tokens as fixed test values. Nothing here is the product's: it only lets a test drive PianoRoll as roll.js
// does and read what it drew and posted.

/** The tokens the roll reads (design/kit/tokens.css names), as fixed test values. */
const TOKENS = {
    surface: "#ffffff",
    "surface-2": "#f3f2ee",
    ink: "#1b1b1f",
    "ink-2": "#55555c",
    "ink-3": "#8a8a90",
    paper: "#fbfaf7",
    notation: "#141414",
    "hairline-strong": "#c9c7c0",
    "on-accent": "#ffffff",
    accent: "#2f5bd3",
    "accent-tint": "#e3eafb",
    teal: "#1f8a86",
    plum: "#7d3c98",
    "warning-fill": "#f6c453",
    "ink-carmine": "#b4233c",
    "ink-ochre": "#b8860b",
    "ink-forest": "#2e6b3a",
    "h-touch": "44px",
    "r-xs": "4px",
    "s-4": "16px",
    "t-slow": "320ms",
    "t-base": "200ms",
    ease: "cubic-bezier(0.2, 0, 0, 1)",
};

/** A value as a line shows it: numbers exactly, strings quoted, a pattern or a canvas by its kind. */
function shown(value) {
    if (typeof value === "number") {
        return String(value);
    }
    if (typeof value === "string") {
        return JSON.stringify(value);
    }
    if (Array.isArray(value)) {
        return `[${value.map(shown).join(",")}]`;
    }
    if (value instanceof FakeCanvas) {
        return "<canvas>";
    }
    return value?.pattern === true ? "<pattern>" : String(value);
}

/** A 2D context that writes each call and each property set down in `log`, in order. */
function recordingContext(log) {
    const state = { fillStyle: "#000000", strokeStyle: "#000000", lineWidth: 1, globalAlpha: 1, font: "10px sans-serif", textAlign: "start", textBaseline: "alphabetic" };
    const own = {
        // A fixed width per letter, so whether a name fits depends on the code alone.
        measureText: (text) => ({ width: String(text).length * 6 }),
        createPattern: (tile, repetition) => {
            log.push(`createPattern ${shown(tile)} ${shown(repetition)}`);
            return { pattern: true };
        },
    };
    return new Proxy(own, {
        get(target, name) {
            if (name in target) {
                return target[name];
            }
            if (name in state) {
                return state[name];
            }
            return (...args) => {
                log.push(`${String(name)} ${args.map(shown).join(" ")}`.trimEnd());
            };
        },
        set(target, name, value) {
            state[name] = value;
            log.push(`${String(name)}=${shown(value)}`);
            return true;
        },
    });
}

class FakeNode {
    constructor() {
        this.parent = null;
    }
}

class FakeText extends FakeNode {
    constructor(text) {
        super();
        this.text = String(text);
    }
}

class FakeElement extends FakeNode {
    constructor(tag) {
        super();
        this.tagName = tag.toUpperCase();
        this.attributes = new Map();
        this.listeners = new Map();
        this.children = [];
        this.style = {};
        this.dataset = {};
        this.className = "";
        this.hidden = false;
        this.disabled = false;
        this.textContent = "";
        this.title = "";
        this.clientWidth = 0;
        this.clientHeight = 0;
        this.offsetTop = 0;
        this.offsetLeft = 0;
        this.offsetHeight = 0;
        this.isConnected = true;
        const classes = new Set();
        this.classList = {
            add: (name) => classes.add(name),
            remove: (name) => classes.delete(name),
            contains: (name) => classes.has(name),
            toggle: (name, on = !classes.has(name)) => {
                if (on) {
                    classes.add(name);
                } else {
                    classes.delete(name);
                }
                return on;
            },
        };
    }

    get offsetParent() {
        return this.parent;
    }

    setAttribute(name, value) {
        this.attributes.set(name, String(value));
    }

    getAttribute(name) {
        return this.attributes.get(name) ?? null;
    }

    hasAttribute(name) {
        return this.attributes.has(name);
    }

    removeAttribute(name) {
        this.attributes.delete(name);
    }

    toggleAttribute(name, on = !this.attributes.has(name)) {
        if (on) {
            this.attributes.set(name, "");
        } else {
            this.attributes.delete(name);
        }
        return on;
    }

    addEventListener(type, listener) {
        const listeners = this.listeners.get(type) ?? [];
        listeners.push(listener);
        this.listeners.set(type, listeners);
    }

    removeEventListener(type, listener) {
        this.listeners.set(type, (this.listeners.get(type) ?? []).filter((each) => each !== listener));
    }

    /** Calls the listeners of an event, as the browser would dispatch it here. */
    dispatch(type, event) {
        for (const listener of this.listeners.get(type) ?? []) {
            listener(event);
        }
    }

    replaceChildren(...nodes) {
        for (const child of this.children) {
            child.parent = null;
        }
        this.children = [];
        this.append(...nodes);
    }

    append(...nodes) {
        for (const node of nodes) {
            node.parent = this;
            this.children.push(node);
        }
    }

    /** The nearest element up from here with an attribute, for selectors of the one form the roll asks: `[name]`. */
    closest(selector) {
        const name = /^\[([\w-]+)\]$/.exec(selector)?.[1];
        for (let at = this; name !== undefined && at instanceof FakeElement; at = at.parent) {
            if (at.hasAttribute(name)) {
                return at;
            }
        }
        return null;
    }

    contains(node) {
        for (let at = node; at !== null && at !== undefined; at = at.parent) {
            if (at === this) {
                return true;
            }
        }
        return false;
    }

    getBoundingClientRect() {
        return { left: 0, top: 0, width: this.clientWidth, height: 0 };
    }

    focus() {}

    setPointerCapture() {}

    releasePointerCapture() {}

    /** The text of this element and all under it, as a reader would get it. */
    get text() {
        return this.children.map((child) => (child instanceof FakeText ? child.text : child.text ?? "")).join("") || this.textContent;
    }
}

class FakeCanvas extends FakeElement {
    constructor() {
        super("canvas");
        this.width = 300;
        this.height = 150;
        this.log = [];
        this.context = recordingContext(this.log);
    }

    getContext() {
        return this.context;
    }
}

/**
 * Puts a fake page on the global object and returns it: the host the roll is made in (`width` by `height`, filling its
 * panel as roll.html's does, data-fill), the device's pixel ratio, a clock (`now`), and `frame(ms)`, which moves the clock
 * on and runs the frames asked for. `practice` sets practice view on the body ("off", "on" or "reveal").
 */
export function fakePage({ width = 480, height = 320, ratio = 2 } = {}) {
    const listeners = new Map();
    const body = new FakeElement("body");
    const host = new FakeElement("div");
    host.setAttribute("data-fill", "true");
    host.dataset.fill = "true";
    host.clientWidth = width;
    host.clientHeight = height;
    body.append(host);
    const page = {
        host,
        body,
        now: 1000,
        frames: [],
        resizers: [],
        frame(ms = 16) {
            page.now += ms;
            for (const run of page.frames.splice(0)) {
                run(page.now);
            }
        },
        resize(newWidth, newHeight = host.clientHeight) {
            host.clientWidth = newWidth;
            host.clientHeight = newHeight;
            for (const run of page.resizers) {
                run();
            }
        },
        practice(value) {
            body.setAttribute("data-practice", value);
        },
    };
    globalThis.document = {
        documentElement: new FakeElement("html"),
        body,
        activeElement: null,
        createElement: (tag) => (tag === "canvas" ? new FakeCanvas() : new FakeElement(tag)),
        createTextNode: (text) => new FakeText(text),
        addEventListener: (type, listener) => listeners.set(type, [...(listeners.get(type) ?? []), listener]),
        removeEventListener: (type, listener) => listeners.set(type, (listeners.get(type) ?? []).filter((each) => each !== listener)),
        dispatchEvent: (event) => (listeners.get(event.type) ?? []).forEach((listener) => listener(event)),
    };
    globalThis.window = { devicePixelRatio: ratio, setTimeout: () => 0, clearTimeout: () => {} };
    globalThis.getComputedStyle = () => ({ getPropertyValue: (name) => TOKENS[name.replace(/^--/, "")] ?? "" });
    globalThis.ResizeObserver = class {
        constructor(run) {
            page.resizers.push(run);
        }

        observe() {}

        disconnect() {}
    };
    globalThis.requestAnimationFrame = (run) => page.frames.push(run);
    globalThis.cancelAnimationFrame = () => {};
    globalThis.Element = FakeElement;
    performance.now = () => page.now;
    return page;
}

/** A pointer event at a point on the roll, as the browser gives the roll's canvas one. */
export function pointer(pointerId, pointerType, clientX, clientY, more = {}) {
    return { pointerId, pointerType, clientX, clientY, shiftKey: false, ctrlKey: false, metaKey: false, preventDefault() {}, ...more };
}

/** The canvases the roll made in its host, by class: the notes ("roll-notes"), the keys ("roll-keys"), the whole piece ("roll-overview"). */
export function canvases(host) {
    return Object.fromEntries(host.children.filter((child) => child instanceof FakeCanvas).map((canvas) => [canvas.className, canvas]));
}
