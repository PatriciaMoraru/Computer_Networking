"""
Performance Experiment for Single-Leader Replication

Lab requirement:
  "Analyze the system performance by making ~100 writes concurrently 
   (10 at a time) on 10 keys"

This script:
  1. Performs 100 writes (10 batches of 10 concurrent writes) to 10 keys
  2. Measures per-write latency
  3. Reports average/median/max latency
  4. Verifies final consistency (leader vs followers)

Run inside the test container:
  python -m tests.test_performance

Or with pytest:
  pytest tests/test_performance.py -v -s
"""

import asyncio
import time
import statistics
from dataclasses import dataclass
from typing import List, Dict, Optional

import httpx

# Configuration
LEADER_URL = "http://leader:8000"
FOLLOWER_HOSTS = ["kv-f1", "kv-f2", "kv-f3", "kv-f4", "kv-f5"]
FOLLOWER_PORT = 8000

NUM_KEYS = 10           # k0..k9
NUM_BATCHES = 10        # 10 batches
WRITES_PER_BATCH = 10   # 10 concurrent writes per batch
# Total writes = NUM_BATCHES * WRITES_PER_BATCH = 100


@dataclass
class WriteResult:
    """Result of a single write operation"""
    key: str
    value: str
    latency_ms: float
    success: bool
    error: Optional[str] = None


def percentile(data: List[float], p: float) -> float:
    """Calculate the p-th percentile of a list of values"""
    if not data:
        return 0.0
    sorted_data = sorted(data)
    k = (len(sorted_data) - 1) * (p / 100)
    f = int(k)
    c = f + 1 if f + 1 < len(sorted_data) else f
    return sorted_data[f] + (k - f) * (sorted_data[c] - sorted_data[f])


@dataclass 
class ExperimentResults:
    """Aggregated results from the performance experiment"""
    total_writes: int
    successful_writes: int
    failed_writes: int
    latencies_ms: List[float]
    
    @property
    def avg_latency_ms(self) -> float:
        return statistics.mean(self.latencies_ms) if self.latencies_ms else 0.0
    
    @property
    def median_latency_ms(self) -> float:
        return statistics.median(self.latencies_ms) if self.latencies_ms else 0.0
    
    @property
    def max_latency_ms(self) -> float:
        return max(self.latencies_ms) if self.latencies_ms else 0.0
    
    @property
    def min_latency_ms(self) -> float:
        return min(self.latencies_ms) if self.latencies_ms else 0.0
    
    @property
    def stdev_latency_ms(self) -> float:
        return statistics.stdev(self.latencies_ms) if len(self.latencies_ms) > 1 else 0.0
    
    @property
    def p95_latency_ms(self) -> float:
        return percentile(self.latencies_ms, 95)
    
    @property
    def p99_latency_ms(self) -> float:
        return percentile(self.latencies_ms, 99)


async def single_write(
    client: httpx.AsyncClient,
    key: str,
    value: str,
) -> WriteResult:
    """Perform a single write and measure latency"""
    start = time.perf_counter()
    try:
        resp = await client.put(
            f"{LEADER_URL}/kv/{key}",
            json={"value": value},
        )
        elapsed_ms = (time.perf_counter() - start) * 1000
        
        if resp.status_code == 200:
            return WriteResult(key=key, value=value, latency_ms=elapsed_ms, success=True)
        else:
            return WriteResult(
                key=key, value=value, latency_ms=elapsed_ms, 
                success=False, error=f"HTTP {resp.status_code}: {resp.text}"
            )
    except Exception as e:
        elapsed_ms = (time.perf_counter() - start) * 1000
        return WriteResult(
            key=key, value=value, latency_ms=elapsed_ms,
            success=False, error=str(e)
        )


async def run_performance_experiment() -> ExperimentResults:
    """
    Run the performance experiment:
    - 10 batches of 10 concurrent writes each
    - Each batch writes to keys k0..k9
    - Total: 100 writes
    """
    all_results: List[WriteResult] = []
    
    async with httpx.AsyncClient(timeout=60.0) as client:
        for batch_num in range(NUM_BATCHES):
            # Prepare 10 concurrent writes for this batch
            tasks = []
            for key_idx in range(WRITES_PER_BATCH):
                key = f"perf-k{key_idx}"
                value = f"batch{batch_num}-value{key_idx}"
                tasks.append(single_write(client, key, value))
            
            # Execute batch concurrently
            batch_results = await asyncio.gather(*tasks)
            all_results.extend(batch_results)
            
            # Small status update
            successful = sum(1 for r in batch_results if r.success)
            print(f"  Batch {batch_num + 1}/{NUM_BATCHES}: {successful}/{len(batch_results)} successful")
    
    # Aggregate results
    successful_results = [r for r in all_results if r.success]
    failed_results = [r for r in all_results if not r.success]
    
    return ExperimentResults(
        total_writes=len(all_results),
        successful_writes=len(successful_results),
        failed_writes=len(failed_results),
        latencies_ms=[r.latency_ms for r in successful_results],
    )


async def verify_consistency() -> Dict[str, bool]:
    """
    Verify that all followers have the same data as the leader.
    Returns a dict mapping follower host to consistency status.
    """
    consistency_results = {}
    
    async with httpx.AsyncClient(timeout=30.0) as client:
        # Get all keys from leader
        leader_data = {}
        for key_idx in range(NUM_KEYS):
            key = f"perf-k{key_idx}"
            try:
                resp = await client.get(f"{LEADER_URL}/kv/{key}")
                if resp.status_code == 200:
                    leader_data[key] = resp.json()["value"]
                else:
                    leader_data[key] = None
            except Exception:
                leader_data[key] = None
        
        print(f"\n  Leader data: {len(leader_data)} keys")
        
        # Compare each follower to leader
        for host in FOLLOWER_HOSTS:
            follower_url = f"http://{host}:{FOLLOWER_PORT}"
            is_consistent = True
            mismatches = []
            
            for key, leader_value in leader_data.items():
                try:
                    resp = await client.get(f"{follower_url}/kv/{key}")
                    if resp.status_code == 200:
                        follower_value = resp.json()["value"]
                    else:
                        follower_value = None
                except Exception:
                    follower_value = None
                
                if follower_value != leader_value:
                    is_consistent = False
                    mismatches.append(f"{key}: leader={leader_value}, follower={follower_value}")
            
            consistency_results[host] = is_consistent
            status = "✓ consistent" if is_consistent else f"✗ INCONSISTENT ({len(mismatches)} mismatches)"
            print(f"  {host}: {status}")
            if mismatches and len(mismatches) <= 3:
                for m in mismatches:
                    print(f"    - {m}")
    
    return consistency_results


async def main():
    """Main entry point for the performance experiment"""
    print("=" * 60)
    print("PERFORMANCE EXPERIMENT: Single-Leader Replication")
    print("=" * 60)
    print(f"Configuration:")
    print(f"  - Keys: {NUM_KEYS} (perf-k0 .. perf-k{NUM_KEYS-1})")
    print(f"  - Batches: {NUM_BATCHES}")
    print(f"  - Writes per batch: {WRITES_PER_BATCH}")
    print(f"  - Total writes: {NUM_BATCHES * WRITES_PER_BATCH}")
    print()
    
    # Run performance test
    print("Running writes...")
    results = await run_performance_experiment()
    
    # Print results
    print()
    print("-" * 60)
    print("RESULTS")
    print("-" * 60)
    print(f"  Total writes:      {results.total_writes}")
    print(f"  Successful:        {results.successful_writes}")
    print(f"  Failed:            {results.failed_writes}")
    print()
    print(f"  Avg latency:       {results.avg_latency_ms:.2f} ms")
    print(f"  Median latency:    {results.median_latency_ms:.2f} ms")
    print(f"  Min latency:       {results.min_latency_ms:.2f} ms")
    print(f"  Max latency:       {results.max_latency_ms:.2f} ms")
    print(f"  P95 latency:       {results.p95_latency_ms:.2f} ms")
    print(f"  P99 latency:       {results.p99_latency_ms:.2f} ms")
    print(f"  Std dev:           {results.stdev_latency_ms:.2f} ms")
    print()
    
    # Verify consistency
    # Wait for background replications to complete before checking
    print("Waiting for background replications to complete...")
    await asyncio.sleep(2)
    
    print("-" * 60)
    print("CONSISTENCY CHECK")
    print("-" * 60)
    consistency = await verify_consistency()
    
    all_consistent = all(consistency.values())
    print()
    if all_consistent:
        print("✓ All followers are consistent with the leader!")
    else:
        inconsistent = [h for h, c in consistency.items() if not c]
        print(f"✗ Inconsistent followers: {inconsistent}")
    
    print()
    print("=" * 60)
    
    # Output in CSV-friendly format for plotting
    print("CSV OUTPUT (for plotting):")
    print("avg_latency_ms,median_latency_ms,min_latency_ms,max_latency_ms,p95_latency_ms,p99_latency_ms,stdev_ms,success_rate")
    success_rate = results.successful_writes / results.total_writes if results.total_writes > 0 else 0
    print(f"{results.avg_latency_ms:.2f},{results.median_latency_ms:.2f},{results.min_latency_ms:.2f},{results.max_latency_ms:.2f},{results.p95_latency_ms:.2f},{results.p99_latency_ms:.2f},{results.stdev_latency_ms:.2f},{success_rate:.2f}")
    
    return results, consistency


# =============================================================================
# Pytest test function
# =============================================================================

def test_performance_experiment():
    """
    Pytest wrapper for the performance experiment.
    Use: pytest tests/test_performance.py -v -s
    """
    results, consistency = asyncio.run(main())
    
    # Assert all writes succeeded
    assert results.failed_writes == 0, f"{results.failed_writes} writes failed"
    
    # Assert all followers are consistent
    inconsistent = [h for h, c in consistency.items() if not c]
    assert not inconsistent, f"Inconsistent followers: {inconsistent}"


# =============================================================================
# Standalone execution
# =============================================================================

if __name__ == "__main__":
    asyncio.run(main())
