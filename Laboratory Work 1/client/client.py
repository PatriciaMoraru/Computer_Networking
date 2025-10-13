import argparse
import os
import socket
from urllib.parse import urlparse, unquote, quote

CRLF = b"\r\n"

def recv_all(sock):
    chunks = []
    while True:
        data = sock.recv(65536)
        if not data:
            break
        chunks.append(data)
    return b"".join(chunks)

def parse_response(raw):
    sep = raw.find(b"\r\n\r\n")
    if sep == -1:
        raise ValueError("Invalid HTTP response: no header/body separator")
    head = raw[:sep].decode("iso-8859-1")
    body = raw[sep+4:]

    lines = head.split("\r\n")
    status_line = lines[0]
    parts = status_line.split(" ", 2)
    if len(parts) < 2 or not parts[1].isdigit():
        raise ValueError("Invalid status line: %r" % status_line)
    status_code = int(parts[1])
    reason = parts[2] if len(parts) >= 3 else ""

    headers = {}
    for line in lines[1:]:
        if not line or ":" not in line:
            continue
        k, v = line.split(":", 1)
        headers[k.strip().lower()] = v.strip()
    return status_code, reason, headers, body

def guess_output_filename(url_path):
    parsed = urlparse(url_path)
    segment = os.path.basename(parsed.path.rstrip("/"))
    segment = unquote(segment)
    if not segment:
        return "download.bin"
    return segment

def build_get_request(host, path):
    if not path.startswith("/"):
        path = "/" + path
    path = quote(path, safe="/%._-~")
    lines = [
        "GET %s HTTP/1.1" % path,
        "Host: %s" % host,
        "Connection: close",
        "",
        "",
    ]
    return ("\r\n".join(lines)).encode("ascii")

def main():
    parser = argparse.ArgumentParser(description="Simple HTTP client for the lab")
    parser.add_argument("server_host", help="Server host (e.g., 127.0.0.1)")
    parser.add_argument("server_port", type=int, help="Server port (e.g., 8000)")
    parser.add_argument("url_path", help="URL path to GET (e.g., /, /index.html, /image.png)")
    parser.add_argument("out_dir", help="Directory to save files (used for PNG/PDF); can be '.'")
    args = parser.parse_args()

    req = build_get_request(args.server_host, args.url_path)

    sock = socket.create_connection((args.server_host, args.server_port), timeout=10)
    sock.sendall(req)
    raw = recv_all(sock)
    sock.close()

    status, reason, headers, body = parse_response(raw)
    ctype = headers.get("content-type", "").lower()

    print("HTTP %d %s" % (status, reason))

    if status >= 400:
        if ctype.startswith("text/"):
            try:
                print(body.decode("utf-8", errors="replace"))
            except Exception:
                pass
        raise SystemExit(1)

    if ctype.startswith("text/html"):
        print(body.decode("utf-8", errors="replace"))
        return

    if ctype in ("image/png", "application/pdf"):
        os.makedirs(args.out_dir, exist_ok=True)
        fname = guess_output_filename(args.url_path)
        out_path = os.path.join(args.out_dir, fname)
        with open(out_path, "wb") as f:
            f.write(body)
        print("Saved:", out_path)
        return

    os.makedirs(args.out_dir, exist_ok=True)
    fname = guess_output_filename(args.url_path)
    out_path = os.path.join(args.out_dir, fname or "download.bin")
    with open(out_path, "wb") as f:
        f.write(body)
    print("Saved (unknown type):", out_path)

if __name__ == "__main__":
    main()