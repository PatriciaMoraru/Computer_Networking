"""
Multi-Quorum Performance Experiment Runner

This script runs the performance experiment for different WRITE_QUORUM values
and collects results into a CSV file for plotting.

IMPORTANT: This script must be run from the HOST machine (not inside Docker),
because it needs to restart containers with different WRITE_QUORUM values.

Usage:
  python tests/run_quorum_experiment.py

Output:
  - results/quorum_experiment.csv  (data for plotting)
  - Console output with results

Note: You can also run the experiment manually by:
  1. Edit docker-compose.yml to set WRITE_QUORUM=N
  2. docker-compose up --build -d
  3. docker exec -it kv-tests python -m tests.test_performance
  4. Record the results
  5. Repeat for N=1,2,3,4,5
"""

import subprocess
import time
import re
import csv
import os
from pathlib import Path
from dataclasses import dataclass
from typing import List, Optional


@dataclass
class QuorumResult:
    """Results for a single WRITE_QUORUM value"""
    write_quorum: int
    avg_latency_ms: float
    median_latency_ms: float
    min_latency_ms: float
    max_latency_ms: float
    stdev_ms: float
    success_rate: float
    all_consistent: bool


def update_docker_compose_quorum(quorum: int, compose_file: str = "docker-compose.yml"):
    """Update WRITE_QUORUM in docker-compose.yml"""
    with open(compose_file, 'r') as f:
        content = f.read()
    
    # Replace WRITE_QUORUM value
    new_content = re.sub(
        r'WRITE_QUORUM:\s*"?\d+"?',
        f'WRITE_QUORUM: "{quorum}"',
        content
    )
    
    with open(compose_file, 'w') as f:
        f.write(new_content)
    
    print(f"  Updated WRITE_QUORUM to {quorum}")


def restart_containers():
    """Restart all containers with new config"""
    print("  Stopping containers...")
    subprocess.run(
        ["docker-compose", "down"],
        capture_output=True,
        check=True
    )
    
    print("  Starting containers...")
    subprocess.run(
        ["docker-compose", "up", "-d", "--build"],
        capture_output=True,
        check=True
    )
    
    # Wait for services to be ready
    print("  Waiting for services to be ready...")
    time.sleep(5)
    
    # Health check
    for _ in range(10):
        result = subprocess.run(
            ["docker", "exec", "kv-tests", "python", "-c", 
             "import httpx; r = httpx.get('http://leader:8000/health'); print(r.status_code)"],
            capture_output=True,
            text=True
        )
        if "200" in result.stdout:
            print("  Services ready!")
            return True
        time.sleep(2)
    
    print("  Warning: Services may not be fully ready")
    return False


def run_performance_test() -> Optional[QuorumResult]:
    """Run the performance test and parse results"""
    print("  Running performance test...")
    
    result = subprocess.run(
        ["docker", "exec", "kv-tests", "python", "-m", "tests.test_performance"],
        capture_output=True,
        text=True,
        timeout=120
    )
    
    output = result.stdout + result.stderr
    print(output)
    
    # Parse CSV output line
    # Format: avg_latency_ms,median_latency_ms,min_latency_ms,max_latency_ms,stdev_ms,success_rate
    csv_match = re.search(
        r'(\d+\.?\d*),(\d+\.?\d*),(\d+\.?\d*),(\d+\.?\d*),(\d+\.?\d*),(\d+\.?\d*)\s*$',
        output,
        re.MULTILINE
    )
    
    if not csv_match:
        print("  ERROR: Could not parse results")
        return None
    
    # Check consistency
    all_consistent = "All followers are consistent" in output
    
    return QuorumResult(
        write_quorum=0,  # Will be set by caller
        avg_latency_ms=float(csv_match.group(1)),
        median_latency_ms=float(csv_match.group(2)),
        min_latency_ms=float(csv_match.group(3)),
        max_latency_ms=float(csv_match.group(4)),
        stdev_ms=float(csv_match.group(5)),
        success_rate=float(csv_match.group(6)),
        all_consistent=all_consistent
    )


def save_results_csv(results: List[QuorumResult], output_file: str):
    """Save results to CSV file"""
    os.makedirs(os.path.dirname(output_file) or '.', exist_ok=True)
    
    with open(output_file, 'w', newline='') as f:
        writer = csv.writer(f)
        writer.writerow([
            'write_quorum', 'avg_latency_ms', 'median_latency_ms', 
            'min_latency_ms', 'max_latency_ms', 'stdev_ms', 
            'success_rate', 'all_consistent'
        ])
        for r in results:
            writer.writerow([
                r.write_quorum, f"{r.avg_latency_ms:.2f}", f"{r.median_latency_ms:.2f}",
                f"{r.min_latency_ms:.2f}", f"{r.max_latency_ms:.2f}", f"{r.stdev_ms:.2f}",
                f"{r.success_rate:.2f}", r.all_consistent
            ])
    
    print(f"\nResults saved to: {output_file}")


def main():
    print("=" * 70)
    print("MULTI-QUORUM PERFORMANCE EXPERIMENT")
    print("=" * 70)
    print()
    print("This will run the performance test for WRITE_QUORUM = 1, 2, 3, 4, 5")
    print("Each run restarts all containers with the new quorum value.")
    print()
    
    results: List[QuorumResult] = []
    quorum_values = [1, 2, 3, 4, 5]
    
    for quorum in quorum_values:
        print("-" * 70)
        print(f"TESTING WRITE_QUORUM = {quorum}")
        print("-" * 70)
        
        try:
            update_docker_compose_quorum(quorum)
            restart_containers()
            
            result = run_performance_test()
            if result:
                result.write_quorum = quorum
                results.append(result)
                print(f"\n  ✓ Quorum {quorum}: avg={result.avg_latency_ms:.2f}ms, consistent={result.all_consistent}")
            else:
                print(f"\n  ✗ Quorum {quorum}: FAILED to get results")
        
        except Exception as e:
            print(f"\n  ✗ Quorum {quorum}: ERROR - {e}")
        
        print()
    
    # Summary
    print("=" * 70)
    print("SUMMARY")
    print("=" * 70)
    print()
    print(f"{'Quorum':<10} {'Avg (ms)':<12} {'Median (ms)':<12} {'Max (ms)':<12} {'Consistent':<12}")
    print("-" * 58)
    for r in results:
        print(f"{r.write_quorum:<10} {r.avg_latency_ms:<12.2f} {r.median_latency_ms:<12.2f} {r.max_latency_ms:<12.2f} {'Yes' if r.all_consistent else 'No':<12}")
    
    # Save to CSV
    if results:
        save_results_csv(results, "results/quorum_experiment.csv")
    
    # Cleanup: restore quorum to 3
    print("\nRestoring WRITE_QUORUM to 3...")
    update_docker_compose_quorum(3)
    
    print("\nDone!")


if __name__ == "__main__":
    main()
