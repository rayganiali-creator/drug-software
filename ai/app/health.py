"""Liveness/readiness. Readiness output never includes exception text or hostnames."""

import asyncio

import httpx
from confluent_kafka import KafkaException
from confluent_kafka.admin import AdminClient
from fastapi import APIRouter, Response

from app.config import Settings, get_settings

router = APIRouter()


async def check_opensearch(settings: Settings) -> str:
    auth = (
        (settings.opensearch_username, settings.opensearch_password or "")
        if settings.opensearch_username
        else None
    )
    try:
        async with httpx.AsyncClient(
            base_url=settings.opensearch_uri.rstrip("/") + "/",
            timeout=settings.health_timeout_seconds,
            auth=auth,
        ) as client:
            res = await client.get("_cluster/health")
        if res.status_code != 200:
            return "Unhealthy"
        return "Healthy" if res.json().get("status") in ("green", "yellow") else "Unhealthy"
    except Exception:  # noqa: BLE001 - any failure means "not ready"; details deliberately dropped
        return "Unhealthy"


def _kafka_blocking(settings: Settings) -> str:
    try:
        admin = AdminClient(
            {
                "bootstrap.servers": settings.kafka_bootstrap_servers,
                "client.id": settings.kafka_client_id,
                "socket.timeout.ms": int(settings.health_timeout_seconds * 1000),
            }
        )
        md = admin.list_topics(timeout=settings.health_timeout_seconds)
        return "Healthy" if md.brokers else "Unhealthy"
    except (KafkaException, Exception):  # noqa: BLE001
        return "Unhealthy"


async def check_kafka(settings: Settings) -> str:
    return await asyncio.to_thread(_kafka_blocking, settings)


@router.get("/health/live")
async def live(response: Response) -> dict[str, object]:
    response.headers["Cache-Control"] = "no-store"
    return {"status": "Healthy", "checks": {}}


@router.get("/health/ready")
async def ready(response: Response) -> dict[str, object]:
    response.headers["Cache-Control"] = "no-store"
    settings = get_settings()
    opensearch, kafka = await asyncio.gather(check_opensearch(settings), check_kafka(settings))
    checks = {"opensearch": opensearch, "kafka": kafka}
    healthy = all(v == "Healthy" for v in checks.values())
    if not healthy:
        response.status_code = 503
    return {"status": "Healthy" if healthy else "Unhealthy", "checks": checks}
