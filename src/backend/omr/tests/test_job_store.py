import secrets
import socket
import time
from collections.abc import Callable, Iterator
from dataclasses import dataclass
from pathlib import Path

import pytest
from azure.storage.blob import BlobServiceClient
from azure.storage.queue import QueueClient, TextBase64DecodePolicy, TextBase64EncodePolicy

from app.azure_store import AzureStore
from app.job_store import FolderStore, LeaseLost, Store
from app.job_worker import Worker
from app.jobs import Jobs, Settings
from tests import stand_ins
from tests.test_jobs import Clock, drain, engines, submit

# Azurite, Azure Storage's emulator, on its usual ports (npx azurite, or its Docker image
# mcr.microsoft.com/azure-storage/azurite); its development account is a well-known one, no secret.
AZURITE = "UseDevelopmentStorage=true"


def _azurite_running() -> bool:
    for port in (10000, 10001):
        try:
            socket.create_connection(("127.0.0.1", port), timeout=0.3).close()
        except OSError:
            return False
    return True


@dataclass
class Harness:
    store: Store
    wait: Callable[[float], None]  # lets that many seconds pass, by the store's clock


@pytest.fixture(params=["folder", "azure"])
def harness(request: pytest.FixtureRequest, tmp_path: Path) -> Iterator[Harness]:
    """The same contract for both stores: FolderStore on a clock of the test's own, AzureStore on Azurite in real time."""
    if request.param == "folder":
        clock = Clock()
        yield Harness(FolderStore(tmp_path, clock), clock.advance)
        return
    if not _azurite_running():
        pytest.skip("Azurite is not running on 127.0.0.1:10000 and 10001 (npx azurite)")
    name = f"t{secrets.token_hex(8)}"
    queue = QueueClient.from_connection_string(
        AZURITE, name, message_encode_policy=TextBase64EncodePolicy(), message_decode_policy=TextBase64DecodePolicy()
    )
    container = BlobServiceClient.from_connection_string(AZURITE).get_container_client(name)
    queue.create_queue()
    container.create_container()
    try:
        yield Harness(AzureStore(queue, container), time.sleep)
    finally:
        queue.delete_queue()
        container.delete_container()


# ---- The contract, for both stores -----------------------------------------------------------------------------


def test_a_received_message_is_leased_and_a_delayed_one_waits_for_its_delay(harness: Harness):
    store = harness.store
    store.send("later", delay=2)
    store.send("first")
    store.send("second")
    assert {store.receive(30).body, store.receive(30).body} == {"first", "second"}
    assert store.receive(30) is None
    harness.wait(2.5)
    assert store.receive(30).body == "later"
    assert store.queue_length() == 3  # leased messages are still in the queue


def test_renewing_a_lease_replaces_its_receipt_and_a_deleted_message_is_gone(harness: Harness):
    store = harness.store
    store.send("unit")
    held = store.receive(30)
    renewed = store.renew(held, 30)
    with pytest.raises(LeaseLost):
        store.delete_message(held)  # its receipt was replaced
    store.delete_message(renewed)
    assert store.queue_length() == 0
    with pytest.raises(LeaseLost):
        store.renew(renewed, 30)
    with pytest.raises(LeaseLost):
        store.delete_message(renewed)


def test_a_message_whose_lease_ran_out_goes_to_the_next_worker_and_the_first_loses_it(harness: Harness):
    store = harness.store
    store.send("unit")
    first = store.receive(1)
    harness.wait(1.5)
    second = store.receive(30)
    assert second.body == "unit"
    with pytest.raises(LeaseLost):
        store.renew(first, 30)
    with pytest.raises(LeaseLost):
        store.delete_message(first)
    store.delete_message(second)
    assert store.queue_length() == 0


def test_blobs_are_replaced_read_listed_and_deleted_by_prefix(harness: Harness):
    store = harness.store
    store.write("jobs/a/units/001-homr.json", b"1")
    store.write("jobs/a/units/001-homr.json", b"2")
    store.write("jobs/ab/job.json", b"{}")
    store.write("jobs/b/job.json", b"{}")
    store.write("engines/homr.json", b"{}")
    assert store.read("jobs/a/units/001-homr.json") == b"2"
    assert store.read("jobs/a/none.json") is None
    assert store.names("jobs/a") == ["jobs/a/units/001-homr.json", "jobs/ab/job.json"]
    assert store.folders("jobs/") == ["jobs/a/", "jobs/ab/", "jobs/b/"]
    store.delete("jobs/a/")  # job a's blobs, none of job ab's
    assert store.names("jobs/") == ["jobs/ab/job.json", "jobs/b/job.json"]
    assert store.folders("jobs/") == ["jobs/ab/", "jobs/b/"]
    assert store.names("engines/") == ["engines/homr.json"]


def test_a_store_that_holds_nothing_answers_with_nothing(harness: Harness):
    store = harness.store
    store.delete("jobs/a/")
    assert store.queue_length() == 0 and store.receive(30) is None
    assert store.names("jobs/") == [] and store.folders("jobs/") == [] and store.read("jobs/a/job.json") is None


def test_a_job_is_read_supervised_and_deleted_through_the_store(harness: Harness, tmp_path: Path):
    jobs = Jobs(harness.store, engines(tmp_path), Settings())
    job_id = submit(jobs, tmp_path, 2)
    jobs.supervise()  # every unit has its message: none is sent a second one
    assert harness.store.queue_length() == 4
    assert drain(Worker(jobs)) == 4
    result = jobs.fetch_result(jobs.find(job_id, None))
    assert [sorted(page["engines"]) for page in result["pages"]] == [["copisteria", "homr"]] * 2
    assert sorted(stand_ins.calls(tmp_path / "homr")) == ["1", "2"]
    jobs.delete(jobs.find(job_id, None))
    assert harness.store.names(f"jobs/{job_id}/") == [] and harness.store.queue_length() == 0


# ---- FolderStore's own ------------------------------------------------------------------------------------------


def test_the_folder_gives_the_oldest_message_first(tmp_path: Path):
    store = FolderStore(tmp_path, Clock())
    for body in ("first", "second", "third"):
        store.send(body)
    assert [store.receive(30).body for _ in range(3)] == ["first", "second", "third"]


def test_a_restarted_store_has_its_messages_and_drops_the_old_leases(tmp_path: Path):
    clock = Clock()
    before = FolderStore(tmp_path, clock)
    before.send("being read")
    before.send("retried later", delay=40)
    before.receive(300)
    after = FolderStore(tmp_path, clock)
    assert after.queue_length() == 2
    assert after.receive(30).body == "being read"
    assert after.receive(30) is None


@pytest.mark.parametrize("name", ["", "../outside", "jobs//x", "/etc/passwd", "jobs/..\\x"])
def test_a_blob_name_stays_inside_the_folder(tmp_path: Path, name: str):
    with pytest.raises(ValueError):
        FolderStore(tmp_path).write(name, b"x")


def test_deleting_a_job_leaves_the_folders_of_others_alone(tmp_path: Path):
    store = FolderStore(tmp_path)
    store.write("jobs/y/units/001-homr.json", b"1")
    store.write("jobs/x/job.json", b"{}")
    # A worker has just made job x's folder for a result and not yet written it.
    (tmp_path / "blobs" / "jobs" / "x" / "results").mkdir()
    store.delete("jobs/y/")
    assert not (tmp_path / "blobs" / "jobs" / "y").exists()
    assert (tmp_path / "blobs" / "jobs" / "x" / "results").is_dir()
    assert store.names("jobs/") == ["jobs/x/job.json"]


def test_a_folder_left_empty_is_listed_so_the_supervisor_removes_it(tmp_path: Path):
    store = FolderStore(tmp_path)
    (tmp_path / "blobs" / "jobs" / "gone" / "results").mkdir(parents=True)
    assert store.folders("jobs/") == ["jobs/gone/"]
    store.delete("jobs/gone/")
    assert store.folders("jobs/") == []


def test_a_store_that_holds_nothing_makes_no_folder(tmp_path: Path):
    store = FolderStore(tmp_path / "jobs")
    assert store.queue_length() == 0 and store.names("jobs/") == [] and store.read("jobs/a/job.json") is None
    store.delete("jobs/a/")
    assert not (tmp_path / "jobs").exists()
