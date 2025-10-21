FROM python:3.11-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    WORKERS=10 \
    DELAY=0.0 \
    COUNTER_MODE=naive \
    COUNTER_DELAY=0.0 \
    RATE_LIMIT=0.0

WORKDIR /app

COPY server/ /app/server/
COPY client/ /app/client/
COPY content/ /app/content/

EXPOSE 8000

CMD ["sh", "-c", "python -m server --host 0.0.0.0 --port 8000 --root /app/content --workers ${WORKERS} --delay ${DELAY} --counter-mode ${COUNTER_MODE} --counter-delay ${COUNTER_DELAY} --rate-limit ${RATE_LIMIT}"]
