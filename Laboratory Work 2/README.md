# HTTP File Server Lab

This project contains a minimal HTTP file server and a simple client, both written in Python, plus Docker assets to run everything easily.

## Architecture

```
┌───────────────┐         ┌───────────────┐
│    client     │────────>│    server     │
│   (Python)    │  HTTP   │   (Python)    │
│               │<────────│               │
└───────────────┘         └───────────────┘
        │                           │
        │                           │
        ▼                           ▼
  downloads/                   content/
  (host)                       (host, read-only)
```

Both containers run in the same Docker network and can reach each other by service names (`server`, `client`).

## Lab Deliverables

### Contents of the source directory

```
Laboratory Work 2/
  client/
    client.py
  content/
    Contemporary Literary Fiction/
      Normal People by Sally Rooney.pdf
    Engineering and Autobiographical Non-Fiction/
      Formula 1 Engines.pdf
      How to Build a Car.pdf
    Fantasy and Romance Series/
      Caraval Trilogy/
        Caraval.pdf
      OUABH/
        Once Upon a Broken Heart.pdf
        fox.png
    Gothic Classics/
      Dracul by Bram Stoker.pdf
      ghost.png
    index.html
    hello.html
  downloads/
  server/
    __main__.py  http_server.py  tcp_server.py  request.py  pathing.py  listing.py
  Dockerfile
  docker-compose.yml
  README.md
```

### Command that runs the server inside the container (with directory argument)

The container runs this command (see `Dockerfile`):

```bash
python -m server --host 0.0.0.0 --port 8000 --root /app/content
```

## Lab 2 — Multithreaded Server and Benchmark (Concurrency)

### What changed in Lab 2
- The TCP server now uses a bounded thread pool to handle requests concurrently.
- Runtime flags control concurrency and simulated work delay:
  - `--workers N` — max in-flight connections handled concurrently.
  - `--delay S` — optional per-request delay to simulate ~S seconds of work.
- A benchmark script (`client/bench.py`) issues concurrent GETs and prints a detailed report.

### Dockerfile (Lab 2)
Environment variables drive the server configuration at runtime.

```startLine:endLine:Laboratory Work 2/Dockerfile
FROM python:3.11-slim

ENV PYTHONDONTWRITEBYTECODE=1 \
    PYTHONUNBUFFERED=1 \
    WORKERS=10 \
    DELAY=0.0 \
    COUNTER_MODE=naive \
    COUNTER_DELAY=0.0

WORKDIR /app

COPY server/ /app/server/
COPY client/ /app/client/
COPY content/ /app/content/

EXPOSE 8000

CMD ["sh", "-c", "python -m server --host 0.0.0.0 --port 8000 --root /app/content --workers ${WORKERS} --delay ${DELAY} --counter-mode ${COUNTER_MODE} --counter-delay ${COUNTER_DELAY}"]
```

### docker-compose (Lab 2)
- Exposes `server` and a `bench` utility service. The optional `client` service is commented out.

```startLine:endLine:Laboratory Work 2/docker-compose.yml
services:
  server:
    build: .
    container_name: http-server-concurrent
    ports:
      - "8000:8000"
    environment:
      - PYTHONUNBUFFERED=1
      - WORKERS=${WORKERS:-10}
      - DELAY=${DELAY:-0.0}
      - COUNTER_MODE=${COUNTER_MODE:-naive}
      - COUNTER_DELAY=${COUNTER_DELAY:-0.0}
    restart: unless-stopped

  bench:
    build: .
    container_name: http-bench
    depends_on:
      - server
    command: ["python","client/bench.py"]
    environment:
      - PYTHONUNBUFFERED=1
      - BENCH_HOST=server
      - BENCH_PORT=8000
      - BENCH_PATH=/index.html
      - BENCH_CONCURRENCY=10
```

Note: When running `docker compose run bench ...EXTRA_ARGS...`, the extra args replace the configured command. To pass flags reliably, invoke the script explicitly as shown below.

### Multithreading quick test
PowerShell:
```powershell
cd "Laboratory Work 2"
set WORKERS = "10" && set DELAY = "1.0"
docker compose up -d --build server
docker compose run --rm bench python client/bench.py --host server --port 8000 --path /index.html --concurrency 10 --timeout 20

set WORKERS = "1" && set DELAY = "1.0"
docker compose up -d --force-recreate server
docker compose run --rm bench python client/bench.py --host server --port 8000 --path /index.html --concurrency 10 --timeout 20
```

Command Prompt (cmd.exe):
```bat
cd "Laboratory Work 2"
set WORKERS=10
set DELAY=1.0
docker compose up -d --build server
docker compose run --rm bench python client/bench.py --host server --port 8000 --path /index.html --concurrency 10 --timeout 20

set WORKERS=1
set DELAY=1.0
docker compose up -d --force-recreate server
docker compose run --rm bench python client/bench.py --host server --port 8000 --path /index.html --concurrency 10 --timeout 20
```

Expected outcome with `DELAY=1.0`:
- `WORKERS=10`: total ≈ 1–2s for 10 requests (parallel).
- `WORKERS=1`: total ≈ 10–12s for 10 requests (sequential).

### Benchmark script output
The script prints a distinct report format, e.g.:

```text
=== HTTP Concurrency Bench ===
Host: server    Port: 8000
URL: /index.html     Concurrency: 10
Running requests...
req#1: 1.041s, 2027 bytes
...

=== Summary ===
Total elapsed: 1.052s
OK/Total: 10/10
Failed: 0
Response time (s): min=1.033  avg=1.040  max=1.045

Report (copy-paste): { ... }
```

### Screenshot placeholders (Lab 2)
- Server up: `docker compose ps` — `screenshots/docker-ps-lab2.png`
- Server logs line showing workers/delay — `screenshots/server_logs_lab2.png`
- Bench (workers=10, delay=1.0) — `screenshots/bench_concurrent.png`
- Bench (workers=1, delay=1.0) — `screenshots/bench_single.png`
- Optional headers from host: `curl -i http://localhost:8000/` — `screenshots/curl_headers.png`

### Troubleshooting
- If bench shows the old one-line output, rebuild or run with `--build`:
  `docker compose run --rm --build bench python client/bench.py ...`
- You can bind-mount the client folder to avoid rebuilds:
  under `bench`, add `volumes: - ./client:/app/client:ro`.
- Extra args to `docker compose run bench ...` replace the configured command; if you want to pass flags, call `python client/bench.py` explicitly as shown above.


## Race Condition Demonstration

This section demonstrates a classic race condition in concurrent programming and how to fix it using synchronization mechanisms.

### Overview

The server includes a **hit counter** that tracks how many times each path (file or directory) is requested. This counter is shared across all worker threads, creating a potential race condition when multiple threads try to increment the same counter simultaneously.

**The Problem:** In naive mode, the counter uses a simple read-modify-write pattern without synchronization:
```python
# Thread-unsafe (naive mode)
previous = self.hits.get(key, 0)  # READ
self.hits[key] = previous + 1     # WRITE (race window!)
```

When multiple threads execute this code concurrently on the same key, they can read the same `previous` value, then all write `previous + 1`, causing **lost updates**.

**The Solution:** Use a lock to ensure atomic read-modify-write operations:
```python
# Thread-safe (locked mode)
with self._hits_lock:
    previous = self.hits.get(key, 0)
    self.hits[key] = previous + 1
```

---

### Test 1: Naive Mode (Demonstrating the Race Condition)

**Configuration:**
- `COUNTER_MODE=naive` — No synchronization
- `COUNTER_DELAY=0.01` — Artificial delay to increase the probability of thread interleaving
- 50 concurrent requests to the same path

**Commands:**
```bat
cd "Laboratory Work 2"
set COUNTER_MODE=naive
set COUNTER_DELAY=0.01
set WORKERS=10
set DELAY=0.0
docker compose up -d --build server
```

Wait for the server to start, then run the benchmark:
```bat
docker compose run --rm bench python client/bench.py --host server --port 8000 --path /index.html --concurrency 50 --timeout 30
```

To view statistics, visit `http://localhost:8000/_stats` in your browser, then check the logs:
```bat
docker compose logs server --tail 50
```

**Results:**

Server logs showing concurrent updates with race condition:
```
http-server-concurrent  | 
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 6 → 7 (⚠️ race possible)[COUNTER:NAIVE]  '/index.html': 7 → 8 (⚠️ ️ race possible)
http-server-concurrent  | Connected by
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 7 → 8 (⚠️ race possible)[COUNTER:NAIVE]  '/index.html': 6 → 7 (⚠️ ️ race possible)
http-server-concurrent  |  ('172.18.0.3', 49078)
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 7 → 8 (⚠️ race possible)
http-server-concurrent  | Connected by
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 7 → 8 (⚠️ race possible)
http-server-concurrent  |  ('172.18.0.3', 49092)
http-server-concurrent  | Connected by[COUNTER:NAIVE]  '/index.html': 7 → 8 (⚠️ race possible)
http-server-concurrent  |  ('172.18.0.3', 49108)
http-server-concurrent  | Connected by[COUNTER:NAIVE]  '/index.html': 8 → 9 (⚠️ race possible) ('172.18.0.3', 49118)       
http-server-concurrent  | Connected by
http-server-concurrent  |  ('172.18.0.3', 49132)
http-server-concurrent  | Connected by ('172.18.0.3', 49142)
http-server-concurrent  | Connected by ('172.18.0.3', 49146)
http-server-concurrent  | Connected by ('172.18.0.3', 49126)
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 8 → 9 (⚠️ race possible)[COUNTER:NAIVE]  '/index.html': 8 → 9 (⚠️ ️ race possible)[COUNTER:NAIVE]  '/index.html': 9 → 10 (⚠️ race possible)
http-server-concurrent  | Connected by ('172.18.0.3', 49154)
http-server-concurrent  |
http-server-concurrent  |
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 9 → 10 (⚠️ race possible)
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 9 → 10 (⚠️ race possible)
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 9 → 10 (⚠️ race possible)
http-server-concurrent  | Connected by ('172.18.0.3', 49164)
http-server-concurrent  | Connected by ('172.18.0.3', 49168)
http-server-concurrent  | Connected by ('172.18.0.3', 49170)
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 9 → 10 (⚠️ race possible)
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 10 → 11 (⚠️ race possible)
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 10 → 11 (⚠️ race possible)
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 10 → 11 (⚠️ race possible)
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 10 → 11 (⚠️ race possible)
http-server-concurrent  | Connected by ('172.18.0.1', 46938)
http-server-concurrent  |
http-server-concurrent  | ================================================================================
http-server-concurrent  | HIT COUNTER STATISTICS
http-server-concurrent  | ================================================================================
http-server-concurrent  | Mode              : NAIVE
http-server-concurrent  | Total Requests    : 50
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 9 → 10 (⚠️ race possible)
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 9 → 10 (⚠️ race possible)
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 9 → 10 (⚠️ race possible)
http-server-concurrent  | Connected by ('172.18.0.3', 49164)
http-server-concurrent  | Connected by ('172.18.0.3', 49168)
http-server-concurrent  | Connected by ('172.18.0.3', 49170)
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 9 → 10 (⚠️ race possible)
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 10 → 11 (⚠️ race possible)
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 10 → 11 (⚠️ race possible)
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 10 → 11 (⚠️ race possible)
http-server-concurrent  | [COUNTER:NAIVE]  '/index.html': 10 → 11 (⚠️ race possible)
http-server-concurrent  | Connected by ('172.18.0.1', 46938)
```
Statistics report:
```
http-server-concurrent  | ================================================================================
http-server-concurrent  | HIT COUNTER STATISTICS
http-server-concurrent  | ================================================================================
http-server-concurrent  | Mode              : NAIVE
http-server-concurrent  | Total Requests    : 50
http-server-concurrent  | Unique Paths      : 1
http-server-concurrent  | Total Recorded Hits: 11
http-server-concurrent  | Lost Updates      : 39 (78.0%)
http-server-concurrent  |                     ⚠️  SIGNIFICANT DATA LOSS - Race condition detected!
http-server-concurrent  | --------------------------------------------------------------------------------
http-server-concurrent  | Top 5 paths by hits:
http-server-concurrent  |     11 hits: /index.html
http-server-concurrent  | ================================================================================
http-server-concurrent  |
http-server-concurrent  | Connected by ('172.18.0.1', 46952)
http-server-concurrent  | Connected by ('172.18.0.1', 46956)
```

**Analysis:**
- **Expected:** 50 requests → 50 hits
- **Actual:** 50 requests → 11 hits
- **Lost Updates:** 39 (78% data loss!)
- **Evidence:** Multiple threads reading the same value simultaneously (e.g., four threads all read `9` and write `10`)

**Screenshots:**

<table>
<tr>
<td width="50%">

![naive1](screenshots/naive1.png)

</td>
<td width="50%">

![naive2](screenshots/naive2.png)

</td>
</tr>
<tr>
<td width="50%">

![naive3](screenshots/naive3.png)

</td>
<td width="50%">

![naive4](screenshots/naive4.png)

</td>
</tr>
<tr>
<td width="50%">

![naive5](screenshots/naive5.png)

</td>
<td width="50%">

![naive6](screenshots/naive6.png)

</td>
</tr>
<tr>
<td colspan="2" align="center">

**🔴 Browser View: Only 11 hits recorded out of 50 requests (78% data loss)**

![naive7](screenshots/naive7.png)

</td>
</tr>
</table>

---

### Test 2: Locked Mode (Fixing the Race Condition)

**Configuration:**
- `COUNTER_MODE=locked` — Thread-safe with locks
- `COUNTER_DELAY=0.01` — Same delay to show locks prevent races even under pressure
- 50 concurrent requests to the same path

**Commands:**
```bat
cd "Laboratory Work 2"
set COUNTER_MODE=locked
set COUNTER_DELAY=0.01
set WORKERS=10
set DELAY=0.0
docker compose up -d --force-recreate server
```

Run benchmark on a different file to distinguish from the naive test:
```bat
docker compose run --rm bench python client/bench.py --host server --port 8000 --path /hello.html --concurrency 50 --timeout 30
```

Visit `http://localhost:8000/_stats` and check logs:
```bat
docker compose logs server --tail 50
```

**Results:**

Server logs showing clean sequential increments:
```
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 28 → 29
http-server-concurrent  | Connected by ('172.18.0.3', 49112)
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 29 → 30
http-server-concurrent  | Connected by ('172.18.0.3', 49118)
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 30 → 31
http-server-concurrent  | Connected by ('172.18.0.3', 49126)
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 31 → 32
http-server-concurrent  | Connected by ('172.18.0.3', 49140)
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 32 → 33
http-server-concurrent  | Connected by ('172.18.0.3', 49146)
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 33 → 34
http-server-concurrent  | Connected by ('172.18.0.3', 49154)
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 34 → 35
http-server-concurrent  | Connected by ('172.18.0.3', 49166)
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 35 → 36
http-server-concurrent  | Connected by ('172.18.0.3', 49180)
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 36 → 37
http-server-concurrent  | Connected by ('172.18.0.3', 49186)
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 37 → 38
http-server-concurrent  | Connected by ('172.18.0.3', 49188)
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 38 → 39
http-server-concurrent  | Connected by ('172.18.0.3', 49204)
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 39 → 40
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 40 → 41
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 41 → 42
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 42 → 43
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 43 → 44
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 44 → 45
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 45 → 46
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 46 → 47
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 47 → 48
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 48 → 49
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 49 → 50
http-server-concurrent  | Connected by ('172.18.0.1', 35524)
http-server-concurrent  |
http-server-concurrent  | ================================================================================
http-server-concurrent  | HIT COUNTER STATISTICS
http-server-concurrent  | ================================================================================
http-server-concurrent  | Mode              : LOCKED
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 39 → 40
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 40 → 41
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 41 → 42
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 42 → 43
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 43 → 44
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 44 → 45
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 45 → 46
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 46 → 47
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 47 → 48
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 48 → 49
http-server-concurrent  | [COUNTER:LOCKED] '/hello.html': 49 → 50
http-server-concurrent  | Connected by ('172.18.0.1', 35524)
```
Statistics report:
```
http-server-concurrent  | ================================================================================
http-server-concurrent  | HIT COUNTER STATISTICS
http-server-concurrent  | ================================================================================
http-server-concurrent  | Mode              : LOCKED
http-server-concurrent  | Total Requests    : 50
http-server-concurrent  | Unique Paths      : 1
http-server-concurrent  | Total Recorded Hits: 50
http-server-concurrent  | Lost Updates      : 0 (0.0%)
http-server-concurrent  |                     ✓ No data loss - Synchronization working!
http-server-concurrent  | Unique Paths      : 1
http-server-concurrent  | Total Recorded Hits: 50
http-server-concurrent  | Lost Updates      : 0 (0.0%)
http-server-concurrent  |                     ✓ No data loss - Synchronization working!
http-server-concurrent  |                     ✓ No data loss - Synchronization working!
http-server-concurrent  | --------------------------------------------------------------------------------
http-server-concurrent  | Top 5 paths by hits:
http-server-concurrent  |     50 hits: /hello.html
http-server-concurrent  | ================================================================================
http-server-concurrent  |
http-server-concurrent  | Connected by ('172.18.0.1', 35530)
```

**Analysis:**
- **Expected:** 50 requests → 50 hits
- **Actual:** 50 requests → 50 hits ✓
- **Lost Updates:** 0 (0% data loss!)
- **Evidence:** Clean sequential increments with no overlapping reads

**Screenshots:**

<table>
<tr>
<td width="50%">

![lock1](screenshots/lock1.png)

</td>
<td width="50%">

![lock2](screenshots/lock2.png)

</td>
</tr>
<tr>
<td width="50%">

![lock3](screenshots/lock3.png)

</td>
<td width="50%">

![lock4](screenshots/lock4.png)

</td>
</tr>
<tr>
<td colspan="2" align="center">

**✅ Browser View: Exactly 50 hits recorded out of 50 requests (0% data loss)**

![lock5](screenshots/lock5.png)

</td>
</tr>
</table>

---

### Conclusion

The lock-based synchronization successfully prevents the race condition:
- **Naive mode:** 78% data loss due to concurrent read-modify-write operations
- **Locked mode:** 0% data loss with proper synchronization

The `threading.Lock()` ensures that only one thread can execute the critical section at a time, preventing lost updates while still allowing concurrent request handling for different paths.
