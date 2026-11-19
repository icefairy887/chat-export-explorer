from pathlib import Path
import argparse
import sqlite3

from storage import get_database_path
from trajectory import build_states


def main():
    parser = argparse.ArgumentParser(description='Build daily trajectory states for the selected archive')
    parser.add_argument('--db', type=Path, help='Same default database as Archive Explorer')
    args = parser.parse_args()
    database = args.db if args.db else get_database_path()
    conn = sqlite3.connect(database.resolve().as_uri() + '?mode=rw', uri=True)
    conn.row_factory = sqlite3.Row
    try:
        count = build_states(conn)
    finally:
        conn.close()
    print(f'Trajectory states built: {count:,} days in {database}')


if __name__ == '__main__':
    main()
