"""Settings come only from environment variables (prefix ``AI_``); nothing secret has a default.

LLM provider / model / embedding settings are intentionally absent: they are undecided (D-17).
"""

from functools import lru_cache

from pydantic import Field
from pydantic_settings import BaseSettings, SettingsConfigDict


class Settings(BaseSettings):
    model_config = SettingsConfigDict(env_prefix="AI_", extra="ignore")

    opensearch_uri: str = Field(
        ..., description="Base URL of OpenSearch, e.g. http://opensearch:9200"
    )
    opensearch_username: str | None = None
    opensearch_password: str | None = None
    kafka_bootstrap_servers: str = Field(..., description="Comma separated host:port list")
    kafka_client_id: str = "medsmarter-ai"
    health_timeout_seconds: float = Field(3.0, gt=0, le=60)
    log_level: str = "INFO"


@lru_cache
def get_settings() -> Settings:
    return Settings()  # type: ignore[call-arg]  # required fields are read from the environment
