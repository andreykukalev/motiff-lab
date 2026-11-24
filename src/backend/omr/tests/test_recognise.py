from pathlib import Path

import pytest

from app import recognise


@pytest.fixture
def host_of_64_cores(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.setattr(recognise.os, "sched_getaffinity", lambda _: set(range(64)), raising=False)


def _cgroup(root: Path, files: dict[str, str]) -> Path:
    for name, text in files.items():
        (root / name).parent.mkdir(parents=True, exist_ok=True)
        (root / name).write_text(text)
    return root


@pytest.mark.parametrize(
    ("files", "expected"),
    [
        ({"cpu.max": "400000 100000\n"}, 4),  # a 4 vCPU Container Apps replica on a large host
        ({"cpu.max": "150000 100000\n"}, 2),  # a part of a core is still a core to use
        ({"cpu.max": "max 100000\n"}, 64),  # no limit
        ({"cpu/cpu.cfs_quota_us": "200000\n", "cpu/cpu.cfs_period_us": "100000\n"}, 2),  # cgroup v1
        ({"cpu/cpu.cfs_quota_us": "-1\n", "cpu/cpu.cfs_period_us": "100000\n"}, 64),  # cgroup v1, no limit
        ({}, 64),  # no cgroup files
    ],
)
def test_the_cores_are_the_ones_the_process_may_run_on_within_its_cpu_quota(
    host_of_64_cores: None, tmp_path: Path, files: dict[str, str], expected: int
):
    assert recognise.cores(_cgroup(tmp_path, files)) == expected


def test_a_quota_above_the_cores_one_may_run_on_gives_those_cores(monkeypatch: pytest.MonkeyPatch, tmp_path: Path):
    monkeypatch.setattr(recognise.os, "sched_getaffinity", lambda _: {0, 1}, raising=False)
    assert recognise.cores(_cgroup(tmp_path, {"cpu.max": "800000 100000"})) == 2
