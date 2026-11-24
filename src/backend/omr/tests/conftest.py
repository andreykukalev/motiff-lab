"""The server's default job folder (main.JOBS_FOLDER) is a folder of this test run's own, removed after it, so the tests
neither read nor leave jobs in the real one."""

import os
import shutil
import tempfile

_FOLDER = tempfile.mkdtemp(prefix="motiff-scan-jobs-tests-")
os.environ["JOBS_FOLDER"] = _FOLDER


def pytest_sessionfinish(session, exitstatus) -> None:  # type: ignore[no-untyped-def]
    shutil.rmtree(_FOLDER, ignore_errors=True)
