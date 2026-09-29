import os

# Set before importing the app: every dependency points at a closed local port.
os.environ.setdefault("AI_OPENSEARCH_URI", "http://127.0.0.1:1")
os.environ.setdefault("AI_KAFKA_BOOTSTRAP_SERVERS", "127.0.0.1:1")
os.environ.setdefault("AI_HEALTH_TIMEOUT_SECONDS", "1")
