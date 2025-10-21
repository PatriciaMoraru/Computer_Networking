import time
import socket
import argparse
import os
from urllib.parse import quote


def send_request(host, port, path="/", timeout=5):
    """Send a single GET request and return (status_code, elapsed_time)."""
    if not path.startswith("/"):
        path = "/" + path
    path = quote(path, safe="/%._-~")
    
    req = (
        f"GET {path} HTTP/1.1\r\n"
        f"Host: {host}\r\n"
        f"Connection: close\r\n\r\n"
    ).encode("ascii")
    
    start = time.perf_counter()
    try:
        s = socket.create_connection((host, port), timeout=timeout)
        s.sendall(req)
        
        buf = b""
        while b"\r\n\r\n" not in buf and len(buf) < 8192:
            chunk = s.recv(4096)
            if not chunk:
                break
            buf += chunk
        s.close()
        
        lines = buf.split(b"\r\n")
        if lines:
            status_line = lines[0].decode("iso-8859-1")
            parts = status_line.split(" ", 2)
            if len(parts) >= 2 and parts[1].isdigit():
                status_code = int(parts[1])
                elapsed = time.perf_counter() - start
                return status_code, elapsed
        
        return None, time.perf_counter() - start
    except Exception as e:
        return None, time.perf_counter() - start


def test_rate_limit(host, port, path="/", requests_per_second=10, duration=5, name="Test"):
    """
    Send requests at a specific rate for a duration.
    Returns stats: {total, successful, blocked, avg_time}
    """
    print(f"\n{'='*80}")
    print(f"{name}: {requests_per_second} req/s for {duration} seconds")
    print(f"{'='*80}")
    
    total_requests = 0
    successful = 0  # 200 OK
    blocked = 0      # 429 Too Many Requests
    errors = 0
    response_times = []
    
    interval = 1.0 / requests_per_second if requests_per_second > 0 else 0
    start_time = time.time()
    
    while time.time() - start_time < duration:
        request_start = time.time()
        
        status, elapsed = send_request(host, port, path)
        total_requests += 1
        response_times.append(elapsed)
        
        if status == 200:
            successful += 1
            print(f"  [{total_requests:3d}] 200 OK       ({elapsed:.3f}s)")
        elif status == 429:
            blocked += 1
            print(f"  [{total_requests:3d}] 429 BLOCKED  ({elapsed:.3f}s)")
        else:
            errors += 1
            print(f"  [{total_requests:3d}] ERROR {status}  ({elapsed:.3f}s)")
        
        # Sleep to maintain target rate
        elapsed_time = time.time() - request_start
        if interval > elapsed_time:
            time.sleep(interval - elapsed_time)
    
    avg_time = sum(response_times) / len(response_times) if response_times else 0
    throughput = successful / duration  # successful requests per second
    
    print(f"\n{'-'*80}")
    print(f"Results for {name}:")
    print(f"  Total Requests    : {total_requests}")
    print(f"  Successful (200)  : {successful}")
    print(f"  Blocked (429)     : {blocked}")
    print(f"  Errors            : {errors}")
    print(f"  Avg Response Time : {avg_time:.3f}s")
    print(f"  Throughput        : {throughput:.2f} req/s (successful only)")
    print(f"{'='*80}\n")
    
    return {
        "total": total_requests,
        "successful": successful,
        "blocked": blocked,
        "errors": errors,
        "avg_time": avg_time,
        "throughput": throughput,
    }


def main():
    parser = argparse.ArgumentParser(description="Rate limit testing for HTTP server")
    parser.add_argument("--host", default=os.getenv("BENCH_HOST", "127.0.0.1"), help="Server host")
    parser.add_argument("--port", type=int, default=int(os.getenv("BENCH_PORT", "8000")), help="Server port")
    parser.add_argument("--path", default="/", help="Request path")
    parser.add_argument("--duration", type=int, default=5, help="Test duration in seconds")
    args = parser.parse_args()
    
    print(f"\nRate Limit Testing")
    print(f"Server: {args.host}:{args.port}")
    print(f"Path: {args.path}")
    print(f"Duration: {args.duration}s per test")
    
    # Test 1: Spam (10 req/s - should exceed 5 req/s limit)
    spam_stats = test_rate_limit(
        args.host, args.port, args.path,
        requests_per_second=10,
        duration=args.duration,
        name="SPAMMER (10 req/s)"
    )
    
    # Test 2: Just below limit (4 req/s - should be fine)
    normal_stats = test_rate_limit(
        args.host, args.port, args.path,
        requests_per_second=4,
        duration=args.duration,
        name="NORMAL USER (4 req/s)"
    )
    
    # Summary comparison
    print("\n" + "=" * 80)
    print("COMPARISON SUMMARY")
    print("=" * 80)
    print(f"{'Metric':<25} {'Spammer (10 req/s)':<25} {'Normal (4 req/s)':<25}")
    print("-" * 80)
    print(f"{'Total Requests':<25} {spam_stats['total']:<25} {normal_stats['total']:<25}")
    print(f"{'Successful (200)':<25} {spam_stats['successful']:<25} {normal_stats['successful']:<25}")
    print(f"{'Blocked (429)':<25} {spam_stats['blocked']:<25} {normal_stats['blocked']:<25}")
    print(f"{'Throughput (req/s)':<25} {spam_stats['throughput']:<25.2f} {normal_stats['throughput']:<25.2f}")
    print("=" * 80)
    
    print("\nConclusion:")
    if spam_stats['blocked'] > 0:
        print(f"✓ Rate limiting is working! Spammer was blocked {spam_stats['blocked']} times.")
    else:
        print("✗ Rate limiting may not be working. Spammer was not blocked.")
    
    if normal_stats['blocked'] == 0:
        print(f"✓ Normal user (below limit) was not blocked.")
    else:
        print(f"⚠ Normal user was blocked {normal_stats['blocked']} times (may need to adjust rate).")


if __name__ == "__main__":
    main()

