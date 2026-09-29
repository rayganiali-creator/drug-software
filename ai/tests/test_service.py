import pytest
from fastapi.testclient import TestClient
from pydantic import ValidationError

from app.config import Settings
from app.main import create_app


@pytest.fixture(scope="module")
def client() -> TestClient:
    return TestClient(create_app())


def test_live_is_healthy(client: TestClient) -> None:
    res = client.get("/health/live")
    assert res.status_code == 200
    assert res.headers["cache-control"] == "no-store"


def test_ready_is_503_without_leaking_details(client: TestClient) -> None:
    res = client.get("/health/ready")
    assert res.status_code == 503
    body = res.json()
    assert body["checks"] == {"opensearch": "Unhealthy", "kafka": "Unhealthy"}
    assert "127.0.0.1" not in res.text
    assert "Exception" not in res.text


def test_version(client: TestClient) -> None:
    assert client.get("/version").json()["service"] == "medsmarter-ai"


def test_safe_correlation_id_is_echoed(client: TestClient) -> None:
    res = client.get("/version", headers={"X-Correlation-Id": "abc-12345678"})
    assert res.headers["x-correlation-id"] == "abc-12345678"


def test_unsafe_correlation_id_is_replaced(client: TestClient) -> None:
    res = client.get("/version", headers={"X-Correlation-Id": 'x y"{evil}'})
    assert res.headers["x-correlation-id"] != 'x y"{evil}'
    assert len(res.headers["x-correlation-id"]) == 32


def test_docs_are_disabled(client: TestClient) -> None:
    assert client.get("/docs").status_code == 404
    assert client.get("/openapi.json").status_code == 404


def test_required_settings_have_no_defaults(monkeypatch: pytest.MonkeyPatch) -> None:
    monkeypatch.delenv("AI_OPENSEARCH_URI", raising=False)
    monkeypatch.delenv("AI_KAFKA_BOOTSTRAP_SERVERS", raising=False)
    with pytest.raises(ValidationError):
        Settings(_env_file=None)  # type: ignore[call-arg]
