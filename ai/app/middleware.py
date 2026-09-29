import logging
import re
import time
import uuid

from starlette.middleware.base import BaseHTTPMiddleware, RequestResponseEndpoint
from starlette.requests import Request
from starlette.responses import Response

from app.logging_config import correlation_id_var

HEADER = "X-Correlation-Id"
_SAFE = re.compile(r"^[A-Za-z0-9\-_.]{8,64}$")
log = logging.getLogger("app.request")


class CorrelationIdMiddleware(BaseHTTPMiddleware):
    """Same contract as the .NET API: accept only safe ids, otherwise generate one."""

    async def dispatch(self, request: Request, call_next: RequestResponseEndpoint) -> Response:
        incoming = request.headers.get(HEADER)
        cid = incoming if incoming and _SAFE.match(incoming) else uuid.uuid4().hex
        token = correlation_id_var.set(cid)
        started = time.perf_counter()
        try:
            response = await call_next(request)
        finally:
            elapsed_ms = (time.perf_counter() - started) * 1000
            # method + path only: no query string, no body.
            if not request.url.path.startswith("/health"):
                log.info("%s %s completed in %.1f ms", request.method, request.url.path, elapsed_ms)
            correlation_id_var.reset(token)
        response.headers[HEADER] = cid
        return response
