"""Where scan jobs wait and lie (decision record 0035, section 3): one small interface over a queue and a store of blobs.

The queue holds only references to work (claim check): a message names a job, a page and an engine. A worker that
receives a message leases it for some seconds and renews the lease while it works; a message whose lease runs out,
because its worker died, can be received again. The blobs hold the pages, the units' states and their results under
names like `jobs/<id>/units/01-homr.json`; writing a name again replaces it, so a repeat overwrites (idempotence).

FolderStore is the player's own computer: the queue in memory, mirrored with the blobs into a folder on disk, so a
restarted server goes on with the jobs it had. It serves one process. The hosted server puts the same interface on an
Azure Storage queue and blob container (item s-3): `receive` is Get Messages with a visibility timeout, `renew` is
Update Message, `delete_message` is Delete Message with its pop receipt, `queue_length` the approximate message count.
"""

from __future__ import annotations

import json
import os
import secrets
import threading
import time
from collections.abc import Callable
from dataclasses import dataclass, replace
from pathlib import Path
from typing import Protocol


@dataclass(frozen=True)
class Message:
    id: str
    receipt: str  # names this lease; renewing gives a new one, as an Azure pop receipt does
    body: str


class LeaseLost(Exception):
    """The message's lease ran out and it went back to the queue, or it was deleted: this worker no longer holds it."""


class Store(Protocol):
    def send(self, body: str, delay: float = 0.0) -> None:
        """Puts a message in the queue, to be received no earlier than `delay` seconds from now."""

    def receive(self, lease: float) -> Message | None:
        """The oldest message that may be received now, leased for `lease` seconds; None when there is none."""

    def renew(self, message: Message, lease: float) -> Message:
        """Leases the message for `lease` seconds from now; raises LeaseLost when the worker no longer holds it."""

    def delete_message(self, message: Message) -> None:
        """Takes the message out of the queue; raises LeaseLost when the worker no longer holds it."""

    def queue_length(self) -> int:
        """Messages in the queue, including those leased or waiting for their delay."""

    def write(self, name: str, data: bytes) -> None:
        """Writes the blob, replacing any of that name. The last writer wins."""

    def read(self, name: str) -> bytes | None:
        """The blob, or None when there is none of that name."""

    def delete(self, prefix: str) -> None:
        """Deletes every blob whose name starts with the prefix."""

    def names(self, prefix: str) -> list[str]:
        """The names of the blobs that start with the prefix, in order."""


@dataclass(frozen=True)
class _Queued:
    body: str
    visible: float  # the time, by the store's clock, from which it may be received
    receipt: str | None = None  # set while it is leased


class FolderStore:
    def __init__(self, folder: Path, clock: Callable[[], float] = time.time) -> None:
        # The folders are made with the first file written in them, so a server that makes no job leaves nothing.
        self._blobs = folder / "blobs"
        self._queue = folder / "queue"
        self._clock = clock
        self._lock = threading.Lock()
        self._messages: dict[str, _Queued] = {}
        for path in sorted(self._queue.glob("*.json")):
            try:
                kept = json.loads(path.read_text(encoding="utf-8"))
            except (OSError, ValueError):
                path.unlink(missing_ok=True)
                continue
            # This store serves one process, so a lease from before a restart has no worker any more.
            self._messages[path.stem] = _Queued(kept["body"], min(kept["visible"], clock()) if kept.get("leased") else kept["visible"])

    # ---- The queue -------------------------------------------------------------------------------------------

    def send(self, body: str, delay: float = 0.0) -> None:
        with self._lock:
            # Names sort in the order sent, so the oldest message is received first.
            name = f"{time.time_ns():020d}-{secrets.token_hex(4)}"
            self._keep(name, _Queued(body, self._clock() + delay))

    def receive(self, lease: float) -> Message | None:
        with self._lock:
            now = self._clock()
            for name, queued in sorted(self._messages.items()):
                if queued.visible <= now:
                    leased = replace(queued, visible=now + lease, receipt=secrets.token_hex(8))
                    self._keep(name, leased)
                    return Message(name, leased.receipt or "", queued.body)
            return None

    def renew(self, message: Message, lease: float) -> Message:
        with self._lock:
            queued = self._held(message)
            renewed = replace(queued, visible=self._clock() + lease, receipt=secrets.token_hex(8))
            self._keep(message.id, renewed)
            return Message(message.id, renewed.receipt or "", message.body)

    def delete_message(self, message: Message) -> None:
        with self._lock:
            self._held(message)
            del self._messages[message.id]
            (self._queue / f"{message.id}.json").unlink(missing_ok=True)

    def queue_length(self) -> int:
        with self._lock:
            return len(self._messages)

    def _held(self, message: Message) -> _Queued:
        queued = self._messages.get(message.id)
        if queued is None or queued.receipt != message.receipt or queued.visible <= self._clock():
            raise LeaseLost()
        return queued

    def _keep(self, name: str, queued: _Queued) -> None:
        self._messages[name] = queued
        kept = {"body": queued.body, "visible": queued.visible, "leased": queued.receipt is not None}
        _write_whole(self._queue / f"{name}.json", json.dumps(kept).encode())

    # ---- The blobs -------------------------------------------------------------------------------------------

    def write(self, name: str, data: bytes) -> None:
        _write_whole(self._path(name), data)

    def read(self, name: str) -> bytes | None:
        try:
            return self._path(name).read_bytes()
        except FileNotFoundError:
            return None

    def delete(self, prefix: str) -> None:
        for name in self.names(prefix):
            self._path(name).unlink(missing_ok=True)
        # The folders under the prefix that are left empty go too, deepest first; os.rmdir leaves one that a writer has
        # just put a file in, and no folder outside the prefix is touched.
        parent = prefix.rsplit("/", 1)[0] if "/" in prefix else ""
        base = self._path(parent) if parent else self._blobs
        if not base.is_dir():
            return
        under = [base, *(path for path in base.rglob("*") if path.is_dir())]
        for folder in sorted(under, key=lambda path: len(path.parts), reverse=True):
            if folder != self._blobs and f"{folder.relative_to(self._blobs).as_posix()}/".startswith(prefix):
                try:
                    os.rmdir(folder)
                except OSError:
                    pass

    def names(self, prefix: str) -> list[str]:
        found = (path.relative_to(self._blobs).as_posix() for path in self._blobs.rglob("*") if path.is_file())
        return sorted(name for name in found if name.startswith(prefix) and not name.endswith(".part"))

    def _path(self, name: str) -> Path:
        parts = name.split("/")
        if not name or any(part in ("", ".", "..") or "\\" in part for part in parts):
            raise ValueError("A blob name is a relative path of plain parts.")
        return self._blobs.joinpath(*parts)


def _write_whole(path: Path, data: bytes) -> None:
    """Writes the file so that a reader, or a restart, sees the old content or the new, never part of it."""
    path.parent.mkdir(parents=True, exist_ok=True)
    part = path.with_name(f"{path.name}.{secrets.token_hex(4)}.part")
    part.write_bytes(data)
    os.replace(part, path)
