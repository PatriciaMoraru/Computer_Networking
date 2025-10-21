import socket
import threading
from concurrent.futures import ThreadPoolExecutor


class TCPServer:
    def __init__(self, host='127.0.0.1', port=8000, max_workers=10):
        self.host = host
        self.port = port
        self.max_workers = max_workers
        # Use a semaphore to bound in-flight connections to the pool size.
        # This prevents unbounded thread growth and applies backpressure under load.
        self._semaphore = threading.Semaphore(self.max_workers)

    def start(self):
        # create a socket object
        s = socket.socket(socket.AF_INET, socket.SOCK_STREAM)

        # allow the socket to reuse the same address immediately after the program closed
        s.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)

        # bind the socket object to the address and port
        s.bind((self.host, self.port))

        # start listening for connections
        s.listen(128)

        print("Listening at", s.getsockname())

        # Thread pool for connection handlers; threads are reused across requests.
        with ThreadPoolExecutor(max_workers=self.max_workers) as executor:
            while True:
                conn, addr = s.accept()
                print("Connected by", addr)

                # Acquire a slot before dispatching work to the pool to ensure
                # at most max_workers connections are processed concurrently.
                self._semaphore.acquire()
                executor.submit(self._handle_connection, conn, addr)

    def _handle_connection(self, conn, addr):
        try:
            data = b""
            max_bytes = 65536
            while b"\r\n\r\n" not in data and len(data) < max_bytes:
                chunk = conn.recv(4096)
                if not chunk:
                    break
                data += chunk

            response = self.handle_request(data)

            conn.sendall(response)
        except Exception:
            # Swallow unexpected errors per connection to avoid crashing the server.
            # In a production server, prefer structured logging here.
            pass
        finally:
            try:
                conn.close()
            except Exception:
                pass
            # Release the semaphore slot so another connection can proceed.
            self._semaphore.release()

    def handle_request(self, data):
        # Default echo behavior; subclasses (e.g., HTTPServer) override this.
        return data
