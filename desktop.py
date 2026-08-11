from __future__ import annotations

import threading
import time
import urllib.request

import webview
from werkzeug.serving import make_server

from app import app
from version import __version__


HOST = "127.0.0.1"
PORT = 5000
URL = f"http://{HOST}:{PORT}"


class ServerThread(threading.Thread):
    def __init__(self):
        super().__init__(daemon=True)
        self.server = make_server(HOST, PORT, app)

    def run(self):
        self.server.serve_forever()

    def shutdown(self):
        self.server.shutdown()


def wait_for_server(timeout: float = 10.0) -> bool:
    deadline = time.time() + timeout

    while time.time() < deadline:
        try:
            urllib.request.urlopen(f"{URL}/health", timeout=1)
            return True
        except Exception:
            time.sleep(0.2)

    return False


def main():
    server = ServerThread()
    server.start()

    if not wait_for_server():
        server.shutdown()
        raise RuntimeError("Chat Export Explorer failed to start.")

    window = webview.create_window(
        f"Chat Export Explorer {__version__}",
        URL,
        width=1280,
        height=850,
        min_size=(900, 650),
    )

    try:
        webview.start()
    finally:
        server.shutdown()


if __name__ == "__main__":
    main()
