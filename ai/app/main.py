from fastapi import FastAPI

from app import __version__
from app.config import get_settings
from app.health import router as health_router
from app.logging_config import configure_logging
from app.middleware import CorrelationIdMiddleware


def create_app() -> FastAPI:
    settings = get_settings()  # fail fast when required env vars are missing
    configure_logging(settings.log_level)
    app = FastAPI(
        title="AI MedSmarter - AI Service",
        version=__version__,
        # No interactive docs in Phase 1: this service is internal-only (mTLS in later phases).
        docs_url=None,
        redoc_url=None,
        openapi_url=None,
    )
    app.add_middleware(CorrelationIdMiddleware)
    app.include_router(health_router)

    @app.get("/version")
    async def version() -> dict[str, str]:
        return {"service": "medsmarter-ai", "version": __version__}

    return app


app = create_app()
