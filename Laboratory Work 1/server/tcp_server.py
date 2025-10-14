import socket

class TCPServer:
    def __init__(self, host='127.0.0.1', port=8000):
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

            data = b""
            max_bytes = 65536
            while b"\r\n\r\n" not in data and len(data) < max_bytes:
                chunk = conn.recv(4096)
                if not chunk:
                    break
                data += chunk

            response = self.handle_request(data)

            conn.sendall(response)
            conn.close()
    
    def handle_request(self, data):
        return data
