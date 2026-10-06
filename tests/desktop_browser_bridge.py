"""Test-only TCP bridge published to Windows loopback for browser validation.
Source is the bridge's allowlisted address. This is not production ACL evidence.
"""
import select
import socket
import socketserver

class Bridge(socketserver.BaseRequestHandler):
    def handle(self):
        with socket.create_connection(('172.30.88.1', 18080), timeout=10) as upstream:
            sockets = [self.request, upstream]
            while True:
                readable, _, _ = select.select(sockets, [], [], 30)
                if not readable:
                    return
                for source in readable:
                    data = source.recv(65536)
                    if not data:
                        return
                    (upstream if source is self.request else self.request).sendall(data)

class Server(socketserver.ThreadingTCPServer):
    allow_reuse_address = True
    daemon_threads = True

with Server(('0.0.0.0', 18080), Bridge) as server:
    server.serve_forever()
