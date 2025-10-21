import argparse
from .http_server import HTTPServer
from .pathing import set_root
from pathlib import Path


def parse_args():
    p = argparse.ArgumentParser(description="Simple HTTP file server (lab)")
    p.add_argument("--host", default="127.0.0.1", help="Host to bind to")
    p.add_argument("--port", default=8000, type=int, help="Port to bind to")
    p.add_argument("--root", default="./content", help="Root directory to serve")
    p.add_argument("--workers", default=10, type=int, help="Max worker threads (bounded thread pool)")
    p.add_argument("--delay", default=0.0, type=float, help="Simulated work delay in seconds")
    p.add_argument("--counter-mode", choices=["naive", "locked"], default="naive",
                   help="Hit counter mode: naive (race) or locked (synchronized)")
    p.add_argument("--counter-delay", default=0.0, type=float,
                   help="Extra delay during counter increment to force interleaving (seconds)")
    p.add_argument("--rate-limit", default=0.0, type=float,
                   help="Rate limit per IP (requests/second, 0 = disabled)")
    return p.parse_args()


if __name__ == "__main__":
    args = parse_args()
    set_root(args.root)
    
    print("=" * 80)
    print("HTTP FILE SERVER - Laboratory Work 2")
    print("=" * 80)
    print(f"Root Directory    : {Path(args.root).resolve()}")
    print(f"Listening on      : {args.host}:{args.port}")
    print(f"Worker Threads    : {args.workers}")
    print(f"Request Delay     : {args.delay}s (simulated work)")
    print("-" * 80)
    print(f"COUNTER MODE      : {args.counter_mode.upper()}")
    if args.counter_mode == "naive":
        print(f"                    ⚠️  RACE CONDITION POSSIBLE (no synchronization)")
    else:
        print(f"                    ✓ Thread-safe (using locks)")
    print(f"Counter Delay     : {args.counter_delay}s (for forcing race interleaving)")
    print("-" * 80)
    print(f"RATE LIMITING     : {args.rate_limit} req/s per IP" if args.rate_limit > 0 else "RATE LIMITING     : Disabled")
    if args.rate_limit > 0:
        print(f"                    ✓ Enabled (thread-safe)")
    print("=" * 80)
    
    server = HTTPServer(
        host=args.host,
        port=args.port,
        max_workers=args.workers,
        simulated_delay_seconds=args.delay,
        counter_mode=args.counter_mode,
        counter_delay=args.counter_delay,
        rate_limit=args.rate_limit,
    )
    server.start()
