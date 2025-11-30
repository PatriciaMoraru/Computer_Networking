# Testing Guide

## Hybrid Testing Approach

This project uses a hybrid testing approach:
- **Leader exposed** on `localhost:8000` for manual testing from host
- **Followers internal** (not exposed to host)
- **Test container** runs inside Docker network with access to all services

---

## Quick Start

### 1. Start the entire system
```bash
docker compose up -d
```

This starts:
- 1 leader (accessible at `localhost:8000` from host)
- 5 followers (internal only)
- 1 test container (keeps running in background)

### 2. Check services are running
```bash
docker compose ps
```

You should see all 7 containers running.

---

## Manual Testing (from Host)

### Test leader directly from your terminal:

```bash
# Check leader health
curl http://localhost:8000/health

# Check role
curl http://localhost:8000/role

# Write a key-value pair (only leader accepts writes)
curl -X PUT http://localhost:8000/kv/test -H "Content-Type: application/json" -d '{"value": "hello"}'

# Read a value
curl http://localhost:8000/kv/test
```

### Test followers (requires exec into container):

```bash
# Check a follower
docker exec kv-f1 curl http://localhost:8000/kv/test

# Or check all followers at once
for i in {1..5}; do
  echo "Follower f$i:"
  docker exec kv-f$i curl -s http://localhost:8000/kv/test
  echo ""
done
```

---

## Automated Testing (from Test Container)

### Run integration tests:
```bash
# Run all tests
docker compose exec tests pytest tests/ -v

# Run specific test
docker compose exec tests pytest tests/test_integration.py::test_replication_eventual_consistency -v

# Run with more output
docker compose exec tests pytest tests/ -v -s
```

### Run performance tests:
```bash
# Once you create perf_test.py
docker compose exec tests python perf_test.py
```

### Interactive testing session:
```bash
# Get a bash shell inside test container
docker compose exec tests bash

# Now you're inside the container, can run:
pytest tests/ -v
python perf_test.py
curl http://leader:8000/health
curl http://kv-f1:8000/health
exit
```

---

## Development Workflow

### Rebuild after code changes:
```bash
# Rebuild all containers
docker compose build

# Restart everything
docker compose down
docker compose up -d
```

### View logs:
```bash
# All services
docker compose logs -f

# Specific service
docker compose logs -f leader
docker compose logs -f f1
docker compose logs -f tests
```

### Test after making changes:
```bash
# Your tests are mounted as volume, so they update automatically
docker compose exec tests pytest tests/test_integration.py -v
```

---

## Changing Configuration

### Test with different write quorum:

Edit `docker-compose.yml`:
```yaml
leader:
  environment:
    WRITE_QUORUM: "5"  # Change this value (1-5)
```

Then restart:
```bash
docker compose down
docker compose up -d
```

### Test with different network delays:

```yaml
leader:
  environment:
    MIN_DELAY_MS: "100"   # minimum delay in ms
    MAX_DELAY_MS: "2000"  # maximum delay in ms
```

---

## Troubleshooting

### Tests can't connect to services:
```bash
# Check all containers are running
docker compose ps

# Check test container can reach leader
docker compose exec tests curl http://leader:8000/health

# Check test container can reach follower
docker compose exec tests curl http://kv-f1:8000/health
```

### Follower not replicating:
```bash
# Check leader logs for replication errors
docker compose logs leader | grep -i error

# Check follower logs
docker compose logs f1
```

### Reset everything:
```bash
# Stop and remove all containers, networks
docker compose down

# Rebuild from scratch
docker compose build --no-cache

# Start fresh
docker compose up -d
```

---

## Running Tests Without Docker (Optional)

If you want to run tests from host (requires leader exposed):

```bash
# Modify test_integration.py to use localhost:
LEADER_URL = "http://localhost:8000"

# Install dependencies locally
pip install -r requirements.txt

# Run tests (will only test leader, not followers)
pytest tests/test_integration.py -v
```

Note: This won't test followers unless you expose their ports.

