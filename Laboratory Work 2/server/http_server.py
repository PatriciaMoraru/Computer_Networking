import os
import mimetypes
import time 
import threading  # lock for fixing race conditions on counters
from .tcp_server import TCPServer
from .request import HTTPRequest
from .pathing import resolve_safe
from .listing import directory_to_links


class HTTPServer(TCPServer):
    headers = {
        'Server': 'Crude Server',
        'Content-Type': 'text/html; charset=utf-8',
    }

    status_codes = {
        200: 'OK',
        400: 'Bad Request',
        404: 'Not Found',
        501: 'Not Implemented',
    }

    mime_overrides = {
        '.html': 'text/html; charset=utf-8',
        '.htm':  'text/html; charset=utf-8',
        '.png':  'image/png',
        '.pdf':  'application/pdf',
    }

    def __init__(self, host='127.0.0.1', port=8000, max_workers=10, simulated_delay_seconds=0.0,
                 counter_mode: str = "naive", counter_delay: float = 0.0):
        # Initialize parent with bounded thread pool configuration.
        super().__init__(host=host, port=port, max_workers=max_workers)
        # Optional artificial delay to simulate per-request work time (not the race demo).
        self.simulated_delay_seconds = simulated_delay_seconds
        # Per-path hit counters (shared across threads in this process).
        self.hits: dict[str, int] = {}
        # Lock used by the lock-based counter mode.
        self._hits_lock = threading.Lock()
        # Counter settings: 'naive' exhibits races; 'locked' protects increments.
        self.counter_mode = counter_mode
        # Delay inserted between read and write in increment to force interlacing.
        self.counter_delay = counter_delay

    # --- Counter utilities ---
    def _normalize_key_from_url(self, url_path: str) -> str:
        # Normalize incoming URL paths and listing hrefs to a single canonical key.
        part = url_path.split('?', 1)[0].split('#', 1)[0]
        from urllib.parse import unquote
        part = unquote(part)
        if not part.startswith('/'):
            part = '/' + part
        return part

    def increment_hit(self, url_path: str):
        # Naive read-modify-write (RMW) versus locked version to demonstrate/fix races.
        key = self._normalize_key_from_url(url_path)
        if self.counter_mode == "locked":
            with self._hits_lock:
                previous = self.hits.get(key, 0)
                if self.counter_delay and self.counter_delay > 0:
                    time.sleep(self.counter_delay)  # force interleaving during demo
                self.hits[key] = previous + 1
        else:
            previous = self.hits.get(key, 0)
            if self.counter_delay and self.counter_delay > 0:
                time.sleep(self.counter_delay)  # naive path: race window
            self.hits[key] = previous + 1

    def get_hits_for_href(self, href: str) -> int:
        # Listing provides hrefs (possibly encoded); map to our normalized key.
        key = self._normalize_key_from_url(href)
        return self.hits.get(key, 0)

    def handle_request(self, data):
        """Handles the incoming request.
        Compiles and returns the response
        """
        try:
            request = HTTPRequest(data)
        except ValueError:
            return self.HTTP_400_handler()

        try:
            handler = getattr(self, 'handle_%s' % request.method)
        except AttributeError:
            handler = self.HTTP_501_handler

        response = handler(request)

        return response

    def HTTP_400_handler(self):
        response_body = b"<h1>400 Bad Request</h1>"
        extra = {
            "Content-Length": str(len(response_body)),
            "Connection": "close",
        }

        response_line = self.response_line(status_code=400)
        response_headers = self.response_headers(extra)
        blank_line = b"\r\n"

        return b"".join([response_line, response_headers, blank_line, response_body])
    
    def HTTP_404_handler(self):
        response_body = b"<h1>404 Not Found</h1>"
        extra = {
            "Content-Length": str(len(response_body)),
            "Connection": "close",
        }
        response_line = self.response_line(status_code=404)
        response_headers = self.response_headers(extra)
        blank_line = b"\r\n"
        return b"".join([response_line, response_headers, blank_line, response_body])

    def HTTP_501_handler(self, request):
        response_body = b"<h1>501 Not Implemented</h1>"
        extra = {
            "Content-Length": str(len(response_body)),
            "Connection": "close",
        }
        response_line = self.response_line(status_code=501)
        response_headers = self.response_headers(extra)
        blank_line = b"\r\n"

        return b"".join([response_line, response_headers, blank_line, response_body])


    def handle_GET(self, request):
        # Optional artificial delay to simulate CPU/IO work; helps demonstrate concurrency.
        if self.simulated_delay_seconds and self.simulated_delay_seconds > 0:
            time.sleep(self.simulated_delay_seconds)

        candidate = resolve_safe(request.uri)
        if candidate is None:
            return self.HTTP_404_handler()
        # Increment hit counter for both directories and files (post path resolution).
        self.increment_hit(request.uri if request.uri else "/")
        if candidate.is_dir():
            # Provide a hits lookup so the listing can show a "Hits" column.
            response_body = directory_to_links(
                candidate,
                request.uri if request.uri else "/",
                get_hits=self.get_hits_for_href,
            )
            extra_headers = {
                "Content-Type": "text/html; charset=utf-8",
                "Content-Length": str(len(response_body)),
                "Connection": "close",
                "Server": "Crude Server"
            }

            response_line = self.response_line(status_code=200)
            response_headers = self.response_headers(extra_headers)
            blank_line = b"\r\n"
            return b"".join([response_line, response_headers, blank_line, response_body])

        if candidate.exists() and candidate.is_file():
            suffix = candidate.suffix.lower()
            allowed_suffixes = {'.html', '.htm', '.png', '.pdf'}
            if suffix not in allowed_suffixes:
                return self.HTTP_404_handler()
            content_type = (
                self.mime_overrides.get(suffix)
                or mimetypes.guess_type(str(candidate))[0]
                or "application/octet-stream"
            )
            with open(candidate, "rb") as f:
                body = f.read()

            extra_headers = {
                "Content-Type": content_type,
                "Content-Length": str(len(body)),
                "Connection": "close",
                "Server": "Crude Server"
            }
            response_line = self.response_line(status_code=200)
            response_headers = self.response_headers(extra_headers)
            blank_line = b"\r\n"
            return b"".join([response_line, response_headers, blank_line, body])

        return self.HTTP_404_handler()

    def response_line(self, status_code):
        """Returns response line"""
        reason = self.status_codes[status_code]
        line = "HTTP/1.1 %s %s\r\n" % (status_code, reason)

        return line.encode() # calling encode to convert str to bytes"
    
    def response_headers(self, extra_headers=None):
        headers_copy = self.headers.copy()

        if extra_headers:
            headers_copy.update(extra_headers)

        headers = ""

        for h in headers_copy:
            headers += "%s: %s\r\n" % (h, headers_copy[h])

        return headers.encode()