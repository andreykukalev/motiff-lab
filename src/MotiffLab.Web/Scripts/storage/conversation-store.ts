// The full assistant's conversations kept on the device (#582 item 17.1, the lead's decision 4): one row per conversation,
// by the piece it is about (the key pencil-store.ts gives a piece: "project:<id>", or "piece:<title>" without a project),
// the record as the text music-core writes (Assistant/Conversation/ConversationRecord.cs). A database of its own, so the
// projects database keeps its version; never in the project file and never synced. Deleting a project deletes its
// conversations (ProjectStore.cs).

import { done, result } from "./idb.js";

const DATABASE = "motiff-lab-assistant";
const VERSION = 1;
const CONVERSATIONS = "conversations";
const BY_PIECE = "piece";

/** What the list of a piece's conversations shows: no question or answer, only the title and when. */
export interface ConversationSummary {
    id: string;
    title: string;
    /** Milliseconds since 1970, when it was last added to. */
    updated: number;
}

interface ConversationRow extends ConversationSummary {
    piece: string;
    record: string;
}

let opening: Promise<IDBDatabase> | null = null;

function open(): Promise<IDBDatabase> {
    opening ??= new Promise((resolve, reject) => {
        const request = indexedDB.open(DATABASE, VERSION);
        request.onupgradeneeded = () => {
            if (!request.result.objectStoreNames.contains(CONVERSATIONS)) {
                request.result.createObjectStore(CONVERSATIONS, { keyPath: "id" }).createIndex(BY_PIECE, BY_PIECE);
            }
        };
        request.onsuccess = () => resolve(request.result);
        request.onerror = () => {
            opening = null;
            reject(request.error);
        };
    });
    return opening;
}

/** The summaries, the conversation added to last first; equal times by id, so the order is the same each time. */
export function newestFirst(rows: ConversationSummary[]): ConversationSummary[] {
    return rows
        .map(({ id, title, updated }) => ({ id, title, updated }))
        .sort((a, b) => b.updated - a.updated || (a.id < b.id ? -1 : a.id > b.id ? 1 : 0));
}

/** Saves a conversation, replacing what was saved under its id. */
export async function saveConversation(piece: string, id: string, title: string, updated: number, record: string): Promise<void> {
    const db = await open();
    const transaction = db.transaction(CONVERSATIONS, "readwrite");
    const row: ConversationRow = { id, piece, title, updated, record };
    transaction.objectStore(CONVERSATIONS).put(row);
    await done(transaction);
}

/** The piece's conversations, newest first. */
export async function listConversations(piece: string): Promise<ConversationSummary[]> {
    const db = await open();
    const rows = await result(db.transaction(CONVERSATIONS).objectStore(CONVERSATIONS).index(BY_PIECE).getAll(piece)) as ConversationRow[];
    return newestFirst(rows);
}

/** The record of a conversation, or null. */
export async function loadConversation(id: string): Promise<string | null> {
    const db = await open();
    const row = await result(db.transaction(CONVERSATIONS).objectStore(CONVERSATIONS).get(id)) as ConversationRow | undefined;
    return row?.record ?? null;
}

/** Forgets every conversation about the piece. */
export async function deleteConversations(piece: string): Promise<void> {
    const db = await open();
    const transaction = db.transaction(CONVERSATIONS, "readwrite");
    const store = transaction.objectStore(CONVERSATIONS);
    for (const key of await result(store.index(BY_PIECE).getAllKeys(piece))) {
        store.delete(key);
    }
    await done(transaction);
}
