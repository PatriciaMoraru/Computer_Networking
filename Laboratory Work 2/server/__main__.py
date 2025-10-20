import argparse
from .http_server import HTTPServer
from .pathing import set_root
from pathlib import Path

def parse_args():
    p = argparse.ArgumentParser(description="Simple HTTP file server (lab)")
    p.add_argument("--host", default="127.0.0.1", help="Host to bind to")
    p.add_argument("--port", default=8000, type=int, help="Port to bind to")
    p.add_argument("--root", default="./content", help="Root directory to serve")
    return p.parse_args()


if __name__ == "__main__":
    args = parse_args()
    set_root(args.root)
    print(f"[server] serving root: {Path(args.root).resolve()} on {args.host}:{args.port}")
    server = HTTPServer(host=args.host, port=args.port)
    server.start()
