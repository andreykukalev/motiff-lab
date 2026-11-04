"""Sign-in for the hosted scan server (decision record 0014).

On the player's own computer nothing is checked. Hosted, AUTH_AUTHORITY and AUTH_AUDIENCE name the
Microsoft Entra External ID tenant and the Motiff Lab API, and a page is read only for a signed-in player
whose access token carries the Scan scope and whose plan is Pro (decision record 0018, section 12): the
API's app role Pro, or a tester's choice in the Motiff-Plan header. The rule and its names are the same as
MotiffLab.MusicCore.Accounts.Plans. The token is checked against the tenant's published keys; nothing
about the player is kept or logged.
"""

from __future__ import annotations

import json
import threading
import urllib.request
from collections.abc import Callable
from typing import Any

import jwt

SCOPE = "Scan"

# The API's app roles and the tester's header, as in MotiffLab.MusicCore.Accounts.Plans.
PRO_ROLE = "Pro"
TESTER_ROLE = "Tester"
PLAN_HEADER = "Motiff-Plan"


class AuthError(Exception):
    """The request may not be read: no token, or one that is not valid for scanning."""


class NotProError(Exception):
    """The sign-in is valid, but the player's plan is not Pro."""


def is_pro(claims: dict[str, Any], choice: str | None) -> bool:
    """Pro by the token's app roles; a tester's choice of "pro" or "free" wins, anyone else's is ignored."""
    roles = claims.get("roles") or []
    if isinstance(roles, str):
        roles = [roles]
    chosen = (choice or "").strip().lower()
    if TESTER_ROLE in roles and chosen in ("pro", "free"):
        return chosen == "pro"
    return PRO_ROLE in roles


def _fetch_json(url: str) -> Any:
    with urllib.request.urlopen(url, timeout=10) as response:  # noqa: S310 (https URLs from configuration)
        return json.load(response)


class TokenVerifier:
    def __init__(self, authority: str, audience: str, fetch_json: Callable[[str], Any] = _fetch_json) -> None:
        self._authority = authority.rstrip("/")
        self._audience = audience
        self._fetch_json = fetch_json
        self._lock = threading.Lock()
        self._issuer: str | None = None
        self._keys: dict[str, jwt.PyJWK] = {}

    def _load(self) -> None:
        config = self._fetch_json(f"{self._authority}/.well-known/openid-configuration")
        keys = self._fetch_json(config["jwks_uri"])
        self._issuer = config["issuer"]
        self._keys = {key["kid"]: jwt.PyJWK(key) for key in keys["keys"] if key.get("use", "sig") == "sig"}

    def _key(self, kid: str) -> jwt.PyJWK:
        with self._lock:
            if kid not in self._keys:
                # First use, or the tenant rolled its keys.
                self._load()
            if kid not in self._keys:
                raise AuthError("The sign-in is not valid. Sign in again.")
            return self._keys[kid]

    def verify(self, authorization: str | None, choice: str | None = None) -> None:
        if not authorization or not authorization.startswith("Bearer "):
            raise AuthError("Sign in to have Motiff Lab's server read your music.")
        token = authorization.removeprefix("Bearer ").strip()
        try:
            key = self._key(jwt.get_unverified_header(token).get("kid", ""))
            claims = jwt.decode(
                token,
                key.key,
                algorithms=["RS256"],
                audience=self._audience,
                issuer=self._issuer,
                options={"require": ["exp", "iss", "aud", "oid"]},
            )
        except jwt.PyJWTError as error:
            raise AuthError("The sign-in is not valid. Sign in again.") from error
        if SCOPE not in str(claims.get("scp", "")).split():
            raise AuthError("This sign-in does not allow reading music on this server.")
        if not is_pro(claims, choice):
            raise NotProError("Reading music on Motiff Lab's server is part of Pro.")
