import os
import mimetypes
from .tcp_server import TCPServer
from .request import HTTPRequest
from .pathing import resolve_safe


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
        # 1) Resolve the requested URI safely under the configured ROOT
        candidate = resolve_safe(request.uri)
        if candidate is None:
            # outside root or ROOT not set
            return self.HTTP_404_handler()
        # 2) If the path is a directory, temporarily serve /index.html from it
        if candidate.is_dir():
            index_path = candidate / "index.html"
            if index_path.exists() and index_path.is_file():
                candidate = index_path
            else:
                return self.HTTP_404_handler()
        # 3) Serve the file if it exists and is a regular file
        if candidate.exists() and candidate.is_file():
            content_type = mimetypes.guess_type(str(candidate))[0] or "application/octet-stream"
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

        # 4) Fallback: not found
        return self.HTTP_404_handler()

    def response_line(self, status_code):
        """Returns response line"""
        reason = self.status_codes[status_code]
        line = "HTTP/1.1 %s %s\r\n" % (status_code, reason)

        return line.encode() # calling encode to convert str to bytes"
    
    def response_headers(self, extra_headers=None):
        """Returns headers
        The 'extra_headers' can be a dict for sending
        extra headers for the current response
        """
        headers_copy = self.headers.copy() # make a local copy of headers

        if extra_headers:
            headers_copy.update(extra_headers)

        headers = ""

        for h in headers_copy:
            headers += "%s: %s\r\n" % (h, headers_copy[h])

        return headers.encode()