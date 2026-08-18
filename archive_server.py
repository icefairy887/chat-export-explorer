from __future__ import annotations

import argparse

from werkzeug.serving import make_server

from app import app


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser(description="Lumina Archive Explorer server")
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, required=True)
    return parser.parse_args()


def main() -> None:
    args = parse_args()
    server = make_server(args.host, args.port, app, threaded=True)
    server.serve_forever()


if __name__ == "__main__":
    main()
