import time
import socket
from concurrent.futures import ThreadPoolExecutor, as_completed


def get(host, port, path="/"):
    """
    Minimal GET using raw sockets; reads until server closes the connection.
    Returns total bytes received (header+body) for sanity checks.
    """
    if not path.startswith("/"):
        path = "/" + path
    req = (
        f"GET {path} HTTP/1.1\r\n"
        f"Host: {host}\r\n"
        f"Connection: close\r\n\r\n"
    ).encode("ascii")

    s = socket.create_connection((host, port), timeout=5)
    s.sendall(req)
    buf = bytearray()
    while True:
        chunk = s.recv(65536)
        if not chunk:
            break
        buf.extend(chunk)
    s.close()
    return len(buf)


def run_bench(host="127.0.0.1", port=8000, path="/", concurrency=10):
    """
    Launches N concurrent GET requests and measures wall time.
    """
    start = time.perf_counter()
    with ThreadPoolExecutor(max_workers=concurrency) as ex:
        futures = [ex.submit(get, host, port, path) for _ in range(concurrency)]
        sizes = [f.result() for f in as_completed(futures)]
    elapsed = time.perf_counter() - start
    print(f"Completed {concurrency} requests in {elapsed:.2f}s; sizes={sizes}")


if __name__ == "__main__":
    # Defaults are fine; tweak as needed, e.g., path="/index.html".
    run_bench()


