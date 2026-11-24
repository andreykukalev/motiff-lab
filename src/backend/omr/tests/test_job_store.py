from pathlib import Path

import pytest

from app.job_store import FolderStore, LeaseLost
from tests.test_jobs import Clock


def test_the_queue_gives_the_oldest_message_that_may_be_received_and_leases_it(tmp_path: Path):
    clock = Clock()
    store = FolderStore(tmp_path, clock)
    store.send("later", delay=10)
    store.send("first")
    store.send("second")
    first = store.receive(30)
    assert first.body == "first"
    assert store.receive(30).body == "second"
    assert store.receive(30) is None
    clock.advance(10)
    assert store.receive(30).body == "later"
    assert store.queue_length() == 3

    renewed = store.renew(first, 30)
    with pytest.raises(LeaseLost):
        store.delete_message(first)  # its receipt was replaced
    store.delete_message(renewed)
    assert store.queue_length() == 2


def test_a_lease_that_ran_out_is_lost_and_the_message_is_received_again(tmp_path: Path):
    clock = Clock()
    store = FolderStore(tmp_path, clock)
    store.send("unit")
    held = store.receive(30)
    clock.advance(30)
    with pytest.raises(LeaseLost):
        store.renew(held, 30)
    assert store.receive(30).body == "unit"


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


def test_blobs_are_replaced_listed_and_deleted_by_prefix(tmp_path: Path):
    store = FolderStore(tmp_path)
    store.write("jobs/a/units/001-homr.json", b"1")
    store.write("jobs/a/units/001-homr.json", b"2")
    store.write("jobs/b/job.json", b"{}")
    assert store.read("jobs/a/units/001-homr.json") == b"2"
    assert store.read("jobs/a/none.json") is None
    assert store.names("jobs/") == ["jobs/a/units/001-homr.json", "jobs/b/job.json"]
    store.delete("jobs/a/")
    assert store.names("jobs/") == ["jobs/b/job.json"]
    assert not (tmp_path / "blobs" / "jobs" / "a").exists()


@pytest.mark.parametrize("name", ["", "../outside", "jobs//x", "/etc/passwd", "jobs/..\\x"])
def test_a_blob_name_stays_inside_the_folder(tmp_path: Path, name: str):
    with pytest.raises(ValueError):
        FolderStore(tmp_path).write(name, b"x")
