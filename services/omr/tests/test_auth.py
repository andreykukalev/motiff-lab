import json
import time

import jwt
import pytest
from cryptography.hazmat.primitives.asymmetric import rsa
from fastapi.testclient import TestClient

from app.auth import AuthError, NotProError, TokenVerifier, is_pro
from app.main import create_app
from app.recognise import Reading
from tests.test_app import png
from tests.test_merge import page

AUTHORITY = "https://tenant.ciamlogin.example/tenant-id/v2.0"
ISSUER = "https://tenant-id.ciamlogin.example/tenant-id/v2.0"
AUDIENCE = "api-client-id"

_key = rsa.generate_private_key(public_exponent=65537, key_size=2048)


def _fetch(url: str):
    if url == f"{AUTHORITY}/.well-known/openid-configuration":
        return {"issuer": ISSUER, "jwks_uri": "https://keys.example/jwks"}
    if url == "https://keys.example/jwks":
        public = json.loads(jwt.algorithms.RSAAlgorithm.to_jwk(_key.public_key()))
        return {"keys": [{**public, "kid": "k1", "use": "sig"}]}
    raise AssertionError(url)


def token(scope="Scan", audience=AUDIENCE, expires_in=300, kid="k1", key=_key, roles=("Pro",)) -> str:
    claims = {"iss": ISSUER, "aud": audience, "oid": "player-1", "scp": scope, "exp": int(time.time()) + expires_in}
    if roles is not None:
        claims["roles"] = list(roles)
    return jwt.encode(claims, key, algorithm="RS256", headers={"kid": kid})


def verifier() -> TokenVerifier:
    return TokenVerifier(AUTHORITY, AUDIENCE, fetch_json=_fetch)


def test_a_signed_in_player_with_the_scan_scope_may_scan():
    verifier().verify(f"Bearer {token()}")


@pytest.mark.parametrize(
    "authorization, reason",
    [
        (None, "Sign in"),
        ("Basic abc", "Sign in"),
        (f"Bearer {token(scope='Projects.ReadWrite')}", "does not allow reading music"),
        (f"Bearer {token(audience='another-api')}", "not valid"),
        (f"Bearer {token(expires_in=-60)}", "not valid"),
        (f"Bearer {token(kid='unknown')}", "not valid"),
        (f"Bearer {token(key=rsa.generate_private_key(public_exponent=65537, key_size=2048))}", "not valid"),
    ],
)
def test_anything_else_is_refused(authorization, reason):
    with pytest.raises(AuthError, match=reason):
        verifier().verify(authorization)


@pytest.mark.parametrize(
    "roles, choice, pro",
    [
        ([], None, False),
        (["Pro"], None, True),
        (["Pro"], "free", True),
        ([], "pro", False),
        (["Tester"], None, False),
        (["Tester"], "pro", True),
        (["Tester", "Pro"], " FREE ", False),
        (["Tester", "Pro"], "gold", True),
        ("Pro", None, True),
    ],
)
def test_pro_follows_the_roles_and_a_testers_choice(roles, choice, pro):
    # The same rule as MotiffLab.MusicCore.Accounts.Plans.Resolve (PlansTests).
    assert is_pro({"roles": roles}, choice) is pro


def test_a_player_without_pro_may_not_scan():
    with pytest.raises(NotProError, match="part of Pro"):
        verifier().verify(f"Bearer {token(roles=None)}")
    verifier().verify(f"Bearer {token(roles=['Tester'])}", "pro")


def test_a_hosted_server_reads_no_page_without_pro():
    client = TestClient(create_app(lambda path, number: Reading(page("1", "2")), verify=verifier().verify))
    refused = client.post(
        "/recognise", files={"file": ("photo.png", png(), "image/png")}, headers={"Authorization": f"Bearer {token(roles=[])}"}
    )
    assert refused.status_code == 403
    assert refused.json() == {"message": "Reading music on Motiff Lab's server is part of Pro.", "code": "pro"}

    testing = client.post(
        "/recognise",
        files={"file": ("photo.png", png(), "image/png")},
        headers={"Authorization": f"Bearer {token(roles=['Tester', 'Pro'])}", "Motiff-Plan": "free"},
    )
    assert testing.status_code == 403


def test_a_hosted_server_reads_no_page_without_a_sign_in():
    read: list[int] = []

    def recognise(path, number):
        read.append(number)
        return Reading(page("1", "2"))

    client = TestClient(create_app(recognise, verify=verifier().verify))
    assert client.get("/health").json()["signIn"] is True

    refused = client.post("/recognise", files={"file": ("photo.png", png(), "image/png")})
    assert refused.status_code == 401
    assert refused.json()["message"] == "Sign in to have Motiff Lab's server read your music."
    assert read == []

    allowed = client.post(
        "/recognise", files={"file": ("photo.png", png(), "image/png")}, headers={"Authorization": f"Bearer {token()}"}
    )
    assert allowed.status_code == 200
    assert read == [1]
