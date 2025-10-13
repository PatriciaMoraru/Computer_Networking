from pathlib import Path
from html import escape

def directory_to_links(dir_path, request_path):
    """
    A simple HTML directory listing for dir_path.
    request_path is the URL path (e.g, "/books/") used for link prefixes.
    """
    title = f"Directory listing for {escape(request_path)}"
    lines = [
        "<!doctype html>",
        "<html><head>",
        f"<title>{title}</title>",
        "</head><body>",
        f"<h1>{title}</h1>",
        "<hr>",
        "<ul>",
    ]

    # Parent directory link (if not root)
    if request_path != "/":
        base = request_path if request_path.endswith("/") else request_path + "/"
        parent = base.rstrip("/").rsplit("/", 1)[0]
        if parent == "":
            parent = "/"
        lines.append(f'<li><a href"{escape(parent) if parent.endswith("/") else escape(parent + "/")}">..</a></li>')

    # List entries sorted by name
    for entry in sorted(dir_path.iterdir(), key=lambda p: p.name.lower()):
        name = entry.name + ("/" if entry.is_dir() else "")
        href = (request_path if request_path.endswith("/") else request_path + "/") + name
        lines.append(f'<li><a href="{escape(href)}">{escape(name)}</a></li>')

    lines += ["</ul>", "<hr>", "</body></html>"]
    html = "\n".join(lines).encode("utf-8")
    return html