from pathlib import Path
from html import escape
from datetime import datetime
from urllib.parse import quote, unquote


def directory_to_links(dir_path, request_path, get_hits=None):
    """
    A styled HTML directory listing for dir_path.
    request_path is the URL path (e.g., "/books/") used for link prefixes.
    get_hits is an optional callable that accepts an href (string) and returns
    an integer number of requests recorded for that path. When provided, the
    listing renders a "Hits" column.
    """
    title = f"Directory listing for {escape(unquote(request_path))}"

    crumbs = [('<a href="/">/</a>', "/")]
    if request_path and request_path != "/":
        decoded = unquote(request_path)
        parts = [p for p in decoded.strip("/").split("/") if p]
        acc_decoded = ""
        for part in parts:
            acc_decoded += "/" + part
            href = quote(acc_decoded, safe="/") + "/"
            crumbs.append((f'<a href="{escape(href)}">{escape(part)}</a>', href))
        if parts:
            crumbs[-1] = (escape(parts[-1]), crumbs[-1][1])
    breadcrumb_html = " / ".join(label for label, _ in crumbs)

    rows = []
    
    if request_path != "/":
        base_decoded = unquote(request_path if request_path else "/")
        base_decoded = base_decoded if base_decoded.endswith("/") else base_decoded + "/"
        parent_decoded = base_decoded.rstrip("/").rsplit("/", 1)[0]
        if parent_decoded == "":
            parent_decoded = "/"
        parent_href = quote(parent_decoded if parent_decoded.endswith("/") else parent_decoded + "/", safe="/")
        rows.append({
            "name": "..",
            "href": parent_href,
            "modified": "",
            "is_dir": True,
            "hits": "",  # parent navigational row: keep hits blank for clarity
        })

    for entry in sorted(dir_path.iterdir(), key=lambda p: (not p.is_dir(), p.name.lower())):
        name = entry.name + ("/" if entry.is_dir() else "")
        base_decoded = unquote(request_path if request_path else "/")
        base_decoded = base_decoded if base_decoded.endswith("/") else base_decoded + "/"
        href = quote(base_decoded + name, safe="/")
        try:
            stat = entry.stat()
            modified = datetime.fromtimestamp(stat.st_mtime).strftime("%Y-%m-%d %H:%M")
        except Exception:
            modified = ""
        rows.append({
            "name": name,
            "href": href,
            "modified": modified,
            "is_dir": entry.is_dir(),
            # Ask the server for hits if a callback is provided; otherwise 0.
            "hits": (get_hits(href) if get_hits else 0),
        })

    lines = [
        "<!doctype html>",
        "<html><head>",
        f"<title>{title}</title>",
        "<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">",
        '<link rel="preconnect" href="https://fonts.googleapis.com">',
        '<link rel="preconnect" href="https://fonts.gstatic.com" crossorigin>',
        '<link href="https://fonts.googleapis.com/css2?family=Press+Start+2P&display=swap" rel="stylesheet">',
        "<style>\n"
        ":root{--bg1:#fceabb;--bg2:#f8b500;--ink:#1a1333;--panel:#ffffffcc;--link:#3b82f6;--linkh:#1d4ed8;}\n"
        "html,body{height:100%;margin:0;}\n"
        "body{font-family:'Press Start 2P',system-ui,Segoe UI,Roboto,Helvetica,Arial,'Noto Sans',sans-serif;"
        "background: linear-gradient(180deg,#2e026d 0%, #8a2be2 35%, #ff7e5f 70%, #feb47b 100%);"
        "color:var(--ink);display:flex;align-items:flex-start;justify-content:center;\n"
        "padding:24px;}\n"
        ".wrap{width:min(1000px,95vw);background:var(--panel);border:4px solid #1a1333;"
        "box-shadow:8px 8px 0 #1a1333;padding:24px;border-radius:6px;}\n"
        "h1{font-size:18px;margin:0 0 12px 0;}\n"
        ".crumbs{font-size:10px;margin-bottom:16px;color:#374151}\n"
        "table{width:100%;border-collapse:separate;border-spacing:0 8px;font-size:12px;}\n"
        "thead th{text-align:left;font-size:10px;color:#6b7280;padding:0 8px;}\n"
        "tbody tr{background:#fff;border:2px solid #1a1333;}\n"
        "tbody td{padding:10px 8px;vertical-align:middle;}\n"
        "a{color:var(--link);text-decoration:none;}\n"
        "a:hover{color:var(--linkh);text-decoration:underline;}\n"
        ".name{display:flex;gap:10px;align-items:center;}\n"
        ".badge{display:inline-block;padding:2px 6px;border:2px solid #1a1333;background:#fde68a;"
        "border-radius:4px;font-size:10px;}\n"
        "</style>",
        "</head><body>",
        f"<div class=\"wrap\">",
        f"<h1>{title}</h1>",
        f"<div class=\"crumbs\">{breadcrumb_html}</div>",
        "<table>",
        # Add a Hits column when a get_hits callback is provided.
        ("<thead><tr><th>Name</th><th>Last Modified</th><th>Hits</th></tr></thead>" if get_hits else "<thead><tr><th>Name</th><th>Last Modified</th></tr></thead>"),
        "<tbody>",
    ]

    for r in rows:
        icon = "📁" if r["is_dir"] else "📄"
        name_html = f"{icon} <a href=\"{escape(r['href'])}\">{escape(r['name'])}</a>"
        if r["is_dir"]:
            name_html += " <span class=\"badge\">DIR</span>"

        if get_hits:
            lines.append(
                "<tr>"
                f"<td class=\"name\">{name_html}</td>"
                f"<td>{escape(r['modified'])}</td>"
                f"<td>{escape(str(r['hits']))}</td>"
                "</tr>"
            )
        else:
            lines.append(
                "<tr>"
                f"<td class=\"name\">{name_html}</td>"
                f"<td>{escape(r['modified'])}</td>"
                "</tr>"
            )

    lines += ["</tbody></table>", "</div>", "</body></html>"]
    html = "\n".join(lines).encode("utf-8")
    return html