"""JSON logging with correlation id. Request bodies / prompts are never logged (PHI safety)."""

import contextvars
import json
import logging
import sys
from datetime import UTC, datetime

correlation_id_var: contextvars.ContextVar[str | None] = contextvars.ContextVar(
    "correlation_id", default=None
)


class JsonFormatter(logging.Formatter):
    def format(self, record: logging.LogRecord) -> str:
        payload: dict[str, object] = {
            "@t": datetime.fromtimestamp(record.created, UTC).isoformat(),
            "@l": record.levelname,
            "@m": record.getMessage(),
            "SourceContext": record.name,
            "Service": "medsmarter-ai",
        }
        cid = correlation_id_var.get()
        if cid:
            payload["CorrelationId"] = cid
        if record.exc_info:
            payload["@x"] = self.formatException(record.exc_info)
        return json.dumps(payload, ensure_ascii=False)


def configure_logging(level: str = "INFO") -> None:
    handler = logging.StreamHandler(sys.stdout)
    handler.setFormatter(JsonFormatter())
    root = logging.getLogger()
    root.handlers[:] = [handler]
    root.setLevel(level.upper())
    # uvicorn's own access log would include query strings; we log requests ourselves.
    logging.getLogger("uvicorn.access").disabled = True
    # httpx logs full URLs at INFO (query strings could carry identifiers later).
    logging.getLogger("httpx").setLevel(logging.WARNING)
