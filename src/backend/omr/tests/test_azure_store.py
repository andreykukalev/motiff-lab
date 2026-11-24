from pathlib import Path
from types import SimpleNamespace

import pytest
from azure.core.exceptions import HttpResponseError, ResourceNotFoundError
from azure.storage.blob import BlobPrefix

from app import azure_store, main
from app.azure_store import AzureStore
from app.job_store import FolderStore, LeaseLost, Message


def _refused(status: int) -> HttpResponseError:
    error = HttpResponseError(message=f"HTTP {status}")
    error.status_code = status
    return error


class QueueStandIn:
    """Records the calls AzureStore makes and answers as the service would, or with the error it is given."""

    def __init__(self, error: Exception | None = None) -> None:
        self.calls: list[tuple[str, dict]] = []
        self.error = error

    def _call(self, name: str, **arguments):  # type: ignore[no-untyped-def]
        self.calls.append((name, arguments))
        if self.error is not None:
            raise self.error

    def send_message(self, content, **arguments):  # type: ignore[no-untyped-def]
        self._call("send", content=content, **arguments)

    def receive_message(self, **arguments):  # type: ignore[no-untyped-def]
        self._call("receive", **arguments)
        return SimpleNamespace(id="m1", pop_receipt="r1", content="body")

    def update_message(self, message, **arguments):  # type: ignore[no-untyped-def]
        self._call("update", message=message, **arguments)
        return SimpleNamespace(pop_receipt="r2")

    def delete_message(self, message, pop_receipt):  # type: ignore[no-untyped-def]
        self._call("delete", message=message, pop_receipt=pop_receipt)

    def get_queue_properties(self):  # type: ignore[no-untyped-def]
        return SimpleNamespace(approximate_message_count=None)


class ContainerStandIn:
    def __init__(self, names: list[str]) -> None:
        self.names = names
        self.deleted: list[str] = []

    def download_blob(self, name):  # type: ignore[no-untyped-def]
        raise ResourceNotFoundError("no blob")

    def list_blob_names(self, name_starts_with):  # type: ignore[no-untyped-def]
        return [name for name in self.names if name.startswith(name_starts_with)]

    def delete_blob(self, name):  # type: ignore[no-untyped-def]
        self.deleted.append(name)
        if name.endswith("gone.json"):
            raise ResourceNotFoundError("deleted by another copy")

    def walk_blobs(self, name_starts_with, delimiter):  # type: ignore[no-untyped-def]
        assert delimiter == "/"
        return [BlobPrefix(None, prefix="jobs/b/"), SimpleNamespace(name="jobs/loose.json"), BlobPrefix(None, prefix="jobs/a/")]


def test_a_lease_is_whole_seconds_never_shorter_and_a_message_expires_a_day_after_its_delay():
    queue = QueueStandIn()
    store = AzureStore(queue, ContainerStandIn([]))  # type: ignore[arg-type]
    store.send("unit", delay=9.2)
    assert store.receive(0.4) == Message("m1", "r1", "body")
    assert store.renew(Message("m1", "r1", "body"), 59.5) == Message("m1", "r2", "body")
    assert queue.calls == [
        ("send", {"content": "unit", "visibility_timeout": 10, "time_to_live": azure_store.MESSAGE_SECONDS + 10}),
        ("receive", {"visibility_timeout": 1}),
        ("update", {"message": "m1", "pop_receipt": "r1", "visibility_timeout": 60}),
    ]
    assert store.queue_length() == 0


@pytest.mark.parametrize("status", [400, 404])
def test_a_receipt_that_no_longer_names_the_message_is_a_lost_lease(status: int):
    store = AzureStore(QueueStandIn(_refused(status)), ContainerStandIn([]))  # type: ignore[arg-type]
    with pytest.raises(LeaseLost):
        store.renew(Message("m1", "r1", "body"), 60)
    with pytest.raises(LeaseLost):
        store.delete_message(Message("m1", "r1", "body"))


def test_any_other_refusal_is_not_taken_for_a_lost_lease():
    store = AzureStore(QueueStandIn(_refused(503)), ContainerStandIn([]))  # type: ignore[arg-type]
    with pytest.raises(HttpResponseError) as raised:
        store.renew(Message("m1", "r1", "body"), 60)
    assert not isinstance(raised.value, LeaseLost) and raised.value.status_code == 503


def test_blobs_missing_or_deleted_meanwhile_are_none_and_folders_are_the_prefixes():
    container = ContainerStandIn(["jobs/a/gone.json", "jobs/a/job.json", "jobs/ab/job.json"])
    store = AzureStore(QueueStandIn(), container)  # type: ignore[arg-type]
    assert store.read("jobs/a/job.json") is None
    store.delete("jobs/a/")
    assert container.deleted == ["jobs/a/gone.json", "jobs/a/job.json"]
    assert store.folders("jobs/") == ["jobs/a/", "jobs/b/"]


def test_the_hosted_server_keeps_its_jobs_in_the_storage_account_it_is_given(monkeypatch):
    monkeypatch.setenv("JOBS_STORAGE_ACCOUNT", "stmotifflabscan")
    monkeypatch.delenv("JOBS_QUEUE", raising=False)
    monkeypatch.setenv("JOBS_CONTAINER", "jobs")
    store = main.jobs_store_from_environment()
    assert isinstance(store, AzureStore)
    assert store._queue.url == "https://stmotifflabscan.queue.core.windows.net/scan-units"
    assert store._container.url == "https://stmotifflabscan.blob.core.windows.net/jobs"
    # Signed in as the managed identity, with no key.
    assert store._queue.credential.__class__.__name__ == "DefaultAzureCredential"


def test_the_player_s_own_server_keeps_its_jobs_in_a_folder(monkeypatch):
    monkeypatch.delenv("JOBS_STORAGE_ACCOUNT", raising=False)
    assert isinstance(main.jobs_store_from_environment(), FolderStore)
    assert main.JOBS_FOLDER == Path(main.os.environ["JOBS_FOLDER"])
