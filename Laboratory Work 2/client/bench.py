import time
import socket
import os
import argparse
from concurrent.futures import ThreadPoolExecutor, as_completed


def get(host, port, path="/", timeout=20):
    """
    Minimal GET using raw sockets; reads until server closes the connection.
    Returns (elapsed_seconds, total_bytes) for reporting.
    """
    if not path.startswith("/"):
        path = "/" + path
    req = (
        f"GET {path} HTTP/1.1\r\n"
        f"Host: {host}\r\n"
        f"Connection: close\r\n\r\n"
    ).encode("ascii")

    start = time.perf_counter()
    s = socket.create_connection((host, port), timeout=timeout)
    s.sendall(req)
    buf = bytearray()
    while True:
        chunk = s.recv(65536)
        if not chunk:
            break
        buf.extend(chunk)
    s.close()
    elapsed = time.perf_counter() - start
    return elapsed, len(buf)


def run_bench(host="127.0.0.1", port=8000, path="/", concurrency=10, timeout=20):
    """
    Launches N concurrent GET requests and prints detailed timings.
    """
    print("=== HTTP Concurrency Bench ===")
    print(f"Host: {host}    Port: {port}")
    print(f"URL: {path}     Concurrency: {concurrency}")
    print("Running requests...")

    per_request_times = []
    sizes = []
    failed = 0

    start = time.perf_counter()
    with ThreadPoolExecutor(max_workers=concurrency) as ex:
        future_to_idx = {}
        for i in range(concurrency):
            fut = ex.submit(get, host, port, path, timeout)
            future_to_idx[fut] = i + 1

        for fut in as_completed(future_to_idx):
            idx = future_to_idx[fut]
            try:
                elapsed, total_bytes = fut.result()
                per_request_times.append(elapsed)
                sizes.append(total_bytes)
                print(f"req#{idx}: {elapsed:.3f}s, {total_bytes} bytes")
            except Exception as e:
                failed += 1
                print(f"Request {idx}: Failed ({e})")

    total_elapsed = time.perf_counter() - start

    print("\n=== Summary ===")
    print(f"Total elapsed: {total_elapsed:.3f}s")
    print(f"OK/Total: {concurrency - failed}/{concurrency}")
    print(f"Failed: {failed}")

    if per_request_times:
        min_t = min(per_request_times)
        max_t = max(per_request_times)
        avg_t = sum(per_request_times) / len(per_request_times)
        print(f"Response time (s): min={min_t:.3f}  avg={avg_t:.3f}  max={max_t:.3f}")

    print("\nReport (copy-paste):")
    summary = {
        "elapsed_total_s": round(total_elapsed, 6),
        "requests": concurrency,
        "ok": concurrency - failed,
        "rt_avg_s": round((sum(per_request_times) / len(per_request_times)) if per_request_times else 0.0, 6),
        "rt_min_s": round(min(per_request_times), 6) if per_request_times else 0.0,
        "rt_max_s": round(max(per_request_times), 6) if per_request_times else 0.0,
        "bytes_each": sizes,
    }
    print(summary)


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description="Concurrent GET benchmark using raw sockets")
    parser.add_argument("--host", default=os.getenv("BENCH_HOST", "127.0.0.1"), help="Target host (service name in Docker, e.g., 'server')")
    parser.add_argument("--port", type=int, default=int(os.getenv("BENCH_PORT", "8000")), help="Target port")
    parser.add_argument("--path", default=os.getenv("BENCH_PATH", "/"), help="URL path (e.g., /, /index.html)")
    parser.add_argument("--concurrency", type=int, default=int(os.getenv("BENCH_CONCURRENCY", "10")), help="Number of concurrent requests")
    parser.add_argument("--timeout", type=int, default=int(os.getenv("BENCH_TIMEOUT", "20")), help="Socket timeout (s)")
    args = parser.parse_args()
    run_bench(args.host, args.port, args.path, args.concurrency, args.timeout)


