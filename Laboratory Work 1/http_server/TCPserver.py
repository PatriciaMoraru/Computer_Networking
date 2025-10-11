import socket

class TCPServer:
    def __init__(self, host='127.0.0.1', port=8888):
        self.host = host
        self.port = port

    def start(self):
        # create a socket object
        s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)

        # allow the socket to reuse the same address immediately after the program closed
        s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)

        # bind the socket object to the address and port
        s.bind((self.host, self.port))

        # start listening for connections
        s.listen(5)

        print("Listening at", s.getsockname())

        while True:
            conn, addr = s.accept()
            print("Connected by", addr)

            # read the data sent by the client
            # we'll read only the first 1024 bytes
            data = conn.recv(4096)

            response = self.handle_request(data)

            conn.sendall(response)
            conn.close()
    
    def handle_request(self, data):
        return data

class HTTPServer(TCPServer):
    headers = {
        'Server': 'Crude Server',
        'Content-Type': 'text/html',
    }

    status_codes = {
        200: 'OK',
        404: 'Not Found',
    }

    def handle_request(self, data):
        """Handles the incoming request.
        Compiles and returns the response
        """
        response_line = b"HTTP/1.1 200 OK\r\n"

        headers = b"".join([
            b"Server: Crude Server\r\n",
            b"Content-Type: text/html\r\n"
        ])

        blank_line = b"\r\n"

        response_body = b"""<html>
            <body>
            <h1>Request received!</h1>
            <body>
            </html>
        """

        return b"".join([response_line, headers, blank_line, response_body])
        

if __name__ == '__main__':
    server = HTTPServer()
    server.start()