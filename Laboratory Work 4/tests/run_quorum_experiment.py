"""
Multi-Quorum Performance Experiment Runner (Fast Mode)

This script runs the performance experiment for different WRITE_QUORUM values
by dynamically changing the quorum via the admin API - NO CONTAINER RESTARTS!

Usage:
  # From inside Docker test container (recommended - much faster!):
  docker exec kv-tests python -m tests.run_quorum_experiment

  # Or from host machine (if containers are running):
  python tests/run_quorum_experiment.py

Output:
  - results/quorum_experiment.csv  (data for plotting)
  - Console output with results
"""

import asyncio
import time
import statistics
import csv
import os
from dataclasses import dataclass
from typing import List, Optional, Dict

import httpx

# Configuration - adjust if running from host vs inside Docker
LEADER_URL = "http://leader:8000"  # Inside Docker network
# LEADER_URL = "http://localhost:8000"  # From host machine

FOLLOWER_HOSTS = ["kv-f1", "kv-f2", "kv-f3", "kv-f4", "kv-f5"]
FOLLOWER_PORT = 8000

NUM_KEYS = 10
NUM_BATCHES = 10
WRITES_PER_BATCH = 10


@dataclass
class WriteResult:
    key: str
    value: str
    latency_ms: float
    success: bool
    error: Optional[str] = None


@dataclass
class QuorumResult:
    """Results for a single WRITE_QUORUM value"""
    write_quorum: int
    avg_latency_ms: float
    median_latency_ms: float
    min_latency_ms: float
    max_latency_ms: float
    p95_latency_ms: float
    p99_latency_ms: float
    stdev_ms: float
    success_rate: float
    consistency_pct: float  # Percentage of matching keys across all replicas


def percentile(data: List[float], p: float) -> float:
    """Calculate the p-th percentile"""
    if not data:
        return 0.0
    sorted_data = sorted(data)
    k = (len(sorted_data) - 1) * (p / 100)
    f = int(k)
    c = f + 1 if f + 1 < len(sorted_data) else f
    return sorted_data[f] + (k - f) * (sorted_data[c] - sorted_data[f])


async def set_quorum(client: httpx.AsyncClient, quorum: int) -> bool:
    """Set the write quorum via admin API"""
    try:
        resp = await client.put(f"{LEADER_URL}/admin/quorum/{quorum}")
        if resp.status_code == 200:
            data = resp.json()
            print(f"  Quorum changed: {data['old_quorum']} -> {data['new_quorum']}")
            return True
        else:
            print(f"  Failed to set quorum: {resp.status_code} - {resp.text}")
            return False
    except Exception as e:
        print(f"  Error setting quorum: {e}")
        return False


async def get_quorum(client: httpx.AsyncClient) -> Optional[int]:
    """Get current write quorum"""
    try:
        resp = await client.get(f"{LEADER_URL}/admin/quorum")
        if resp.status_code == 200:
            return resp.json()["write_quorum"]
    except Exception:
        pass
    return None


async def single_write(client: httpx.AsyncClient, key: str, value: str) -> WriteResult:
    """Perform a single write and measure latency"""
    start = time.perf_counter()
    try:
        resp = await client.put(f"{LEADER_URL}/kv/{key}", json={"value": value})
        elapsed_ms = (time.perf_counter() - start) * 1000
        
        if resp.status_code == 200:
            return WriteResult(key=key, value=value, latency_ms=elapsed_ms, success=True)
        else:
            return WriteResult(key=key, value=value, latency_ms=elapsed_ms, 
                             success=False, error=f"HTTP {resp.status_code}")
    except Exception as e:
        elapsed_ms = (time.perf_counter() - start) * 1000
        return WriteResult(key=key, value=value, latency_ms=elapsed_ms,
                         success=False, error=str(e))


async def run_writes(client: httpx.AsyncClient) -> List[WriteResult]:
    """Run 100 writes (10 batches of 10 concurrent writes)"""
    all_results: List[WriteResult] = []
    
    for batch_num in range(NUM_BATCHES):
        tasks = []
        for key_idx in range(WRITES_PER_BATCH):
            key = f"exp-k{key_idx}"
            value = f"q-batch{batch_num}-v{key_idx}"
            tasks.append(single_write(client, key, value))
        
        batch_results = await asyncio.gather(*tasks)
        all_results.extend(batch_results)
    
    return all_results


async def verify_consistency(client: httpx.AsyncClient) -> float:
    """
    Check consistency between leader and all followers.
    Returns percentage of matching key-value pairs (0.0 to 100.0).
    
    Total comparisons = NUM_KEYS × len(FOLLOWER_HOSTS) = 10 × 5 = 50
    """
    # Get leader data
    leader_data = {}
    for key_idx in range(NUM_KEYS):
        key = f"exp-k{key_idx}"
        try:
            resp = await client.get(f"{LEADER_URL}/kv/{key}")
            if resp.status_code == 200:
                leader_data[key] = resp.json()["value"]
        except Exception:
            pass
    
    if not leader_data:
        return 0.0
    
    # Compare with each follower and count matches
    total_comparisons = 0
    matching_pairs = 0
    
    for host in FOLLOWER_HOSTS:
        follower_url = f"http://{host}:{FOLLOWER_PORT}"
        for key, leader_value in leader_data.items():
            total_comparisons += 1
            try:
                resp = await client.get(f"{follower_url}/kv/{key}")
                if resp.status_code == 200:
                    follower_value = resp.json()["value"]
                    if follower_value == leader_value:
                        matching_pairs += 1
            except Exception:
                pass  # Count as mismatch
    
    # Calculate percentage
    if total_comparisons == 0:
        return 0.0
    return (matching_pairs / total_comparisons) * 100.0


async def run_experiment_for_quorum(client: httpx.AsyncClient, quorum: int) -> Optional[QuorumResult]:
    """Run the full experiment for a specific quorum value"""
    
    # Set quorum
    if not await set_quorum(client, quorum):
        return None
    
    # Small delay to ensure quorum change is effective
    await asyncio.sleep(0.1)
    
    # Run writes
    print(f"  Running {NUM_BATCHES * WRITES_PER_BATCH} writes...")
    results = await run_writes(client)
    
    # Calculate metrics
    successful = [r for r in results if r.success]
    latencies = [r.latency_ms for r in successful]
    
    if not latencies:
        print("  ERROR: No successful writes!")
        return None
    
    # Wait for background replications
    print("  Waiting for background replications...")
    await asyncio.sleep(2)
    
    # Check consistency (returns percentage)
    consistency_pct = await verify_consistency(client)
    
    return QuorumResult(
        write_quorum=quorum,
        avg_latency_ms=statistics.mean(latencies),
        median_latency_ms=statistics.median(latencies),
        min_latency_ms=min(latencies),
        max_latency_ms=max(latencies),
        p95_latency_ms=percentile(latencies, 95),
        p99_latency_ms=percentile(latencies, 99),
        stdev_ms=statistics.stdev(latencies) if len(latencies) > 1 else 0,
        success_rate=len(successful) / len(results),
        consistency_pct=consistency_pct
    )


def save_results_csv(results: List[QuorumResult], output_file: str):
    """Save results to CSV file"""
    os.makedirs(os.path.dirname(output_file) or '.', exist_ok=True)
    
    with open(output_file, 'w', newline='') as f:
        writer = csv.writer(f)
        writer.writerow([
            'write_quorum', 'avg_latency_ms', 'median_latency_ms', 
            'min_latency_ms', 'max_latency_ms', 'p95_latency_ms', 'p99_latency_ms',
            'stdev_ms', 'success_rate', 'consistency_pct'
        ])
        for r in results:
            writer.writerow([
                r.write_quorum, f"{r.avg_latency_ms:.2f}", f"{r.median_latency_ms:.2f}",
                f"{r.min_latency_ms:.2f}", f"{r.max_latency_ms:.2f}", 
                f"{r.p95_latency_ms:.2f}", f"{r.p99_latency_ms:.2f}",
                f"{r.stdev_ms:.2f}", f"{r.success_rate:.2f}", f"{r.consistency_pct:.1f}"
            ])
    
    print(f"\nResults saved to: {output_file}")


async def main():
    print("=" * 70)
    print("MULTI-QUORUM PERFORMANCE EXPERIMENT (Fast Mode)")
    print("=" * 70)
    print()
    print("This runs experiments for WRITE_QUORUM = 1, 2, 3, 4, 5")
    print("Using dynamic quorum change - NO container restarts needed!")
    print()
    
    results: List[QuorumResult] = []
    quorum_values = [1, 2, 3, 4, 5]
    
    async with httpx.AsyncClient(timeout=60.0) as client:
        # Check connection
        try:
            resp = await client.get(f"{LEADER_URL}/health")
            if resp.status_code != 200:
                print("ERROR: Cannot connect to leader!")
                return
            print(f"Connected to leader: {LEADER_URL}")
            print()
        except Exception as e:
            print(f"ERROR: Cannot connect to leader: {e}")
            print("Make sure containers are running: docker-compose up -d")
            return
        
        # Get initial quorum
        initial_quorum = await get_quorum(client)
        print(f"Initial quorum: {initial_quorum}")
        print()
        
        start_time = time.time()
        
        for quorum in quorum_values:
            print("-" * 70)
            print(f"TESTING WRITE_QUORUM = {quorum}")
            print("-" * 70)
            
            try:
                result = await run_experiment_for_quorum(client, quorum)
                if result:
                    results.append(result)
                    print(f"\n  ✓ Quorum {quorum}: avg={result.avg_latency_ms:.1f}ms, "
                          f"median={result.median_latency_ms:.1f}ms, consistency={result.consistency_pct:.1f}%")
                else:
                    print(f"\n  ✗ Quorum {quorum}: FAILED")
            except Exception as e:
                print(f"\n  ✗ Quorum {quorum}: ERROR - {e}")
            
            print()
        
        elapsed = time.time() - start_time
        
        # Restore original quorum
        if initial_quorum:
            print(f"Restoring quorum to {initial_quorum}...")
            await set_quorum(client, initial_quorum)
    
    # Summary
    print("=" * 70)
    print("SUMMARY")
    print("=" * 70)
    print()
    print(f"{'Quorum':<8} {'Mean':<10} {'Median':<10} {'P95':<10} {'P99':<10} {'Consistency':<12}")
    print("-" * 70)
    for r in results:
        print(f"{r.write_quorum:<8} {r.avg_latency_ms:<10.1f} {r.median_latency_ms:<10.1f} "
              f"{r.p95_latency_ms:<10.1f} {r.p99_latency_ms:<10.1f} {r.consistency_pct:<10.1f}%")
    
    print()
    print(f"Total experiment time: {elapsed:.1f} seconds")
    
    # Save to CSV
    if results:
        save_results_csv(results, "results/quorum_experiment.csv")
    
    print("\nDone!")


if __name__ == "__main__":
    asyncio.run(main())
