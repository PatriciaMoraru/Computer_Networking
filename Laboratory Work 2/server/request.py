class HTTPRequest:
    def __init__(self, data):
        self.method = None
        self.uri = None
        self.http_version = "1.1"

        # call self.parse() method to parse the request data
        self.parse(data)

    def parse(self, data):
        lines = data.split(b"\r\n")
        request_line = lines[0]

        if not request_line:
            raise ValueError("Empty request line")

        # Split the request line into exactly three parts: METHOD SP URI SP HTTP/VERSION
        # Using maxsplit=2 preserves any spaces inside the URI (common in our content paths).
        words = request_line.split(b" ", 2)

        if len(words) < 3 or not words[0] or not words[1] or not words[2]:
            raise ValueError("Malformed request line")

        self.method = words[0].decode()

        # URI present
        self.uri = words[1].decode()

        # HTTP version
        self.http_version = words[2].decode(errors="ignore")
        