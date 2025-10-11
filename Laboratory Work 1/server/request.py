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

        words = request_line.split(b" ")

        self.method = words[0].decode()

        if len(words) > 1:
            # we put this in an if-block because sometimes
            # browsers don't send uri for homepage
            self.uri = words[1].decode()

        if len(words) > 2:
            self.http_version = words[2]