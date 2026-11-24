"""The scan jobs' store on the hosted server (decision record 0035, section 3): job_store.Store on an Azure Storage queue
and a blob container, so that every copy of the server works the same jobs and a restarted copy goes on with them.

- The queue holds the units' messages: `send` is Put Message, its delay the message's first visibility timeout;
  `receive` is Get Messages for one message, leased for its visibility timeout; `renew` is Update Message with the
  message's pop receipt, which gives the next receipt; `delete_message` is Delete Message with it. A receipt that no
  longer names the message (its lease ran out and another worker received it, or it was deleted) is LeaseLost.
  `queue_length` is the queue's approximate message count, leased and delayed messages included. The queue keeps no
  order: the oldest message is usually, not always, the first received.
- The blobs hold everything else under the same names as FolderStore's files, one blob a name, written whole (Put Blob),
  so a reader sees the old content or the new and the last writer wins. `folders` lists by the delimiter "/", so the
  supervisor reads one name a job rather than every blob.

The server signs in with the container app's managed identity (DefaultAzureCredential; AZURE_CLIENT_ID names the
user-assigned identity): no key or connection string is in the code or the settings. The account deletes a blob a day
after it was last written (infra/scan.bicep), the supervisor sooner (jobs.py); a message expires a day after it was sent.
"""

from __future__ import annotations

import math

from azure.core.exceptions import HttpResponseError, ResourceNotFoundError
from azure.storage.blob import BlobPrefix, ContainerClient
from azure.storage.queue import QueueClient, TextBase64DecodePolicy, TextBase64EncodePolicy

from app.job_store import LeaseLost, Message

# A unit's message outlives its job by no more than its delay: a job is deleted a day after it was made at the latest.
MESSAGE_SECONDS = 24 * 3600


class AzureStore:
    def __init__(self, queue: QueueClient, container: ContainerClient) -> None:
        self._queue = queue
        self._container = container

    @classmethod
    def for_account(cls, account: str, queue: str, container: str) -> AzureStore:
        """The store in the named storage account, signed in as the server's managed identity."""
        from azure.identity import DefaultAzureCredential

        credential = DefaultAzureCredential()
        return cls(
            QueueClient(
                f"https://{account}.queue.core.windows.net",
                queue,
                credential=credential,
                message_encode_policy=TextBase64EncodePolicy(),
                message_decode_policy=TextBase64DecodePolicy(),
            ),
            ContainerClient(f"https://{account}.blob.core.windows.net", container, credential=credential),
        )

    # ---- The queue -------------------------------------------------------------------------------------------

    def send(self, body: str, delay: float = 0.0) -> None:
        wait = math.ceil(max(0.0, delay))
        self._queue.send_message(body, visibility_timeout=wait, time_to_live=MESSAGE_SECONDS + wait)

    def receive(self, lease: float) -> Message | None:
        message = self._queue.receive_message(visibility_timeout=_seconds(lease))
        return None if message is None else Message(message.id, message.pop_receipt, message.content)

    def renew(self, message: Message, lease: float) -> Message:
        try:
            renewed = self._queue.update_message(message.id, pop_receipt=message.receipt, visibility_timeout=_seconds(lease))
        except HttpResponseError as error:
            raise _lost(error) from error
        return Message(message.id, renewed.pop_receipt, message.body)

    def delete_message(self, message: Message) -> None:
        try:
            self._queue.delete_message(message.id, message.receipt)
        except HttpResponseError as error:
            raise _lost(error) from error

    def queue_length(self) -> int:
        return self._queue.get_queue_properties().approximate_message_count or 0

    # ---- The blobs -------------------------------------------------------------------------------------------

    def write(self, name: str, data: bytes) -> None:
        self._container.upload_blob(name, data, overwrite=True)

    def read(self, name: str) -> bytes | None:
        try:
            return self._container.download_blob(name).readall()
        except ResourceNotFoundError:
            return None

    def delete(self, prefix: str) -> None:
        # The prefix is used as it is given: "jobs/a/" names job a's blobs and none of job ab's.
        for name in self.names(prefix):
            try:
                self._container.delete_blob(name)
            except ResourceNotFoundError:
                pass  # deleted by another copy of the server meanwhile

    def names(self, prefix: str) -> list[str]:
        return sorted(self._container.list_blob_names(name_starts_with=prefix))

    def folders(self, prefix: str) -> list[str]:
        found = self._container.walk_blobs(name_starts_with=prefix, delimiter="/")
        return sorted(item.name for item in found if isinstance(item, BlobPrefix))


def _seconds(lease: float) -> int:
    """A visibility timeout in whole seconds, never shorter than the lease asked for."""
    return max(1, math.ceil(lease))


def _lost(error: HttpResponseError) -> Exception:
    """The receipt no longer names the message: 404 when it is gone, 400 when it was received again (another receipt)."""
    return LeaseLost() if error.status_code in (400, 404) else error
