import asyncio
import time

import httpx
import pytest

# When running inside Docker network, use container names
# When running from host, use localhost for leader only
LEADER_URL = "http://leader:8000"
FOLLOWER_HOSTS = ["kv-f1", "kv-f2", "kv-f3", "kv-f4", "kv-f5"]
FOLLOWER_PORT = 8000


def _follower_url(host: str) -> str:
    """Build follower URL using container name (works inside Docker network)"""
    return f"http://{host}:{FOLLOWER_PORT}"


# =============================================================================
# Test 1: Followers must reject writes (403)
# =============================================================================

def test_followers_reject_writes():
    """
    Verify that all followers reject PUT requests with 403 Forbidden.
    Only the leader should accept writes.
    """
    with httpx.Client(timeout=10.0) as client:
        for host in FOLLOWER_HOSTS:
            url = f"{_follower_url(host)}/kv/test-key"
            resp = client.put(url, json={"value": "should-fail"})
            
            assert resp.status_code == 403, (
                f"Follower {host} should reject writes with 403, got {resp.status_code}"
            )
            assert "leader" in resp.json().get("detail", "").lower(), (
                f"Follower {host} error message should mention 'leader'"
            )


# =============================================================================
# Test 2: Leader accepts writes and quorum succeeds
# =============================================================================

def test_leader_accepts_writes():
    """
    Verify that the leader accepts writes and returns 200 when quorum is met.
    With 5 followers and WRITE_QUORUM=3, this should succeed.
    """
    with httpx.Client(timeout=10.0) as client:
        # Verify we're talking to the leader
        role_resp = client.get(f"{LEADER_URL}/role")
        assert role_resp.status_code == 200
        assert role_resp.json()["role"] == "leader"
        
        # Write should succeed
        key, value = "leader-test-key", "leader-test-value"
        resp = client.put(f"{LEADER_URL}/kv/{key}", json={"value": value})
        
        assert resp.status_code == 200, f"Leader should accept writes, got {resp.status_code}"
        assert resp.json()["status"] == "ok"
        
        # Verify we can read it back
        get_resp = client.get(f"{LEADER_URL}/kv/{key}")
        assert get_resp.status_code == 200
        assert get_resp.json()["value"] == value


# =============================================================================
# Test 3: Concurrent writes don't break the system
# =============================================================================

def test_concurrent_writes():
    """
    Verify the system handles multiple concurrent writes without errors.
    This is a smaller-scale version of the performance test.
    """
    num_keys = 5
    num_writes_per_key = 3
    
    async def do_concurrent_writes():
        async with httpx.AsyncClient(timeout=30.0) as client:
            tasks = []
            for i in range(num_keys):
                for j in range(num_writes_per_key):
                    key = f"concurrent-k{i}"
                    value = f"v{i}-{j}"
                    task = client.put(f"{LEADER_URL}/kv/{key}", json={"value": value})
                    tasks.append(task)
            
            responses = await asyncio.gather(*tasks, return_exceptions=True)
            return responses
    
    responses = asyncio.run(do_concurrent_writes())
    
    # All writes should succeed (status 200)
    errors = []
    for i, resp in enumerate(responses):
        if isinstance(resp, Exception):
            errors.append(f"Request {i} raised exception: {resp}")
        elif resp.status_code != 200:
            errors.append(f"Request {i} returned {resp.status_code}: {resp.text}")
    
    assert not errors, f"Some concurrent writes failed:\n" + "\n".join(errors)


# =============================================================================
# Test 4: Replication & eventual consistency (original test, slightly improved)
# =============================================================================

def test_replication_eventual_consistency():
    """
    Very rough sketch:

    1. write some keys to leader
    2. wait a bit
    3. check that followers have same values
    """

    keys_and_values = {f"k{i}": f"v{i}" for i in range(5)}

    with httpx.Client() as client:
        # 1) write to leader
        for k, v in keys_and_values.items():
            resp = client.put(f"{LEADER_URL}/kv/{k}", json={"value": v})
            assert resp.status_code == 200

        # 2) wait for replication to complete (or do smarter polling)
        time.sleep(2)

        # 3) verify on leader and followers
        for k, v in keys_and_values.items():
            # leader
            r_leader = client.get(f"{LEADER_URL}/kv/{k}")
            assert r_leader.status_code == 200
            assert r_leader.json()["value"] == v

            # followers (you might need extra config to reach them from host)
            for host in FOLLOWER_HOSTS:
                # adjust host or port mapping to how you expose followers
                url = _follower_url(host)
                r_f = client.get(f"{url}/kv/{k}")
                # depending on network setup this may need tweaking
                assert r_f.status_code == 200
                assert r_f.json()["value"] == v
