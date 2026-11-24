"""Reads one page with one engine in a process of its own, so that the unit's time limit can stop it (0035, section 3).

A thread cannot be stopped from outside, and an engine that hangs would keep its worker for ever. So the engine's
`read` runs in a child process that leads a process group of its own: the engine's own programs (homr, copisteria) are
in it too, and at the time limit the whole group is killed. The child is forked from multiprocessing's fork server,
a clean process started once, never from the server's threads; so an engine's `read` must be picklable: a function of
a module, or a functools.partial of one. While the child works, `tick` is called every `tick_seconds` (the worker
renews its lease with it). The child sends back the reading (with copisteria's page evidence), or the reason it could
not read, and nothing else: an engine's own messages may quote the page's text, so none of them leave the child.
"""

from __future__ import annotations

import multiprocessing
import os
import signal
import time
from collections.abc import Callable
from dataclasses import asdict
from pathlib import Path

from app.recognise import Found, Reading, RecogniseError

_CONTEXT = multiprocessing.get_context("forkserver")


def read_within(
    read: Callable[[Path, int], Reading],
    image: Path,
    number: int,
    seconds: float,
    tick: Callable[[], None] = lambda: None,
    tick_seconds: float = 20.0,
) -> Reading:
    """The engine's reading of the page; RecogniseError when it could not read it, crashed or ran past `seconds`."""
    receiver, sender = _CONTEXT.Pipe(duplex=False)
    child = _CONTEXT.Process(target=_read_in_child, args=(read, image, number, sender), daemon=True)
    child.start()
    sender.close()
    deadline = time.monotonic() + seconds
    try:
        while not receiver.poll(max(0.0, min(tick_seconds, deadline - time.monotonic()))):
            if time.monotonic() >= deadline:
                raise RecogniseError(f"Reading page {number} took too long and was stopped.")
            if not child.is_alive() and not receiver.poll():
                raise RecogniseError(f"Page {number} could not be read.")
            tick()
        try:
            kind, payload, found, evidence = receiver.recv()
        except EOFError as error:
            raise RecogniseError(f"Page {number} could not be read.") from error
    finally:
        _stop(child)
        receiver.close()
    if kind == "read":
        return Reading(payload, Found(**found), evidence)
    raise RecogniseError(payload, Found(**found) if found else None)


def _read_in_child(read: Callable[[Path, int], Reading], image: Path, number: int, sender) -> None:  # type: ignore[no-untyped-def]
    os.setsid()
    try:
        reading = read(image, number)
        sender.send(("read", reading.musicxml, asdict(reading.found), reading.evidence))
    except RecogniseError as error:
        # The engines' own messages to the player name the step that failed, never the music (recognise.py).
        sender.send(("error", str(error), asdict(error.found), None))
    except Exception:  # noqa: BLE001 (anything else an engine raises is told as a page that could not be read)
        sender.send(("error", f"Page {number} could not be read.", None, None))
    finally:
        sender.close()


def _stop(child: multiprocessing.process.BaseProcess) -> None:
    """Kills the child's process group, the engine's programs with it, and waits for the child."""
    if child.pid is not None:
        try:
            os.killpg(child.pid, signal.SIGKILL)
        except (ProcessLookupError, PermissionError):
            # The child is gone, or has not yet led its own group.
            child.kill()
    child.join()
