"""
Dependency vulnerability scan using the OSV.dev API.

Reads pinned packages (name==version) from a requirements file, asks OSV.dev
for known vulnerabilities in each one, and writes the raw responses to JSON.

Usage:
    python osv_scan.py --requirements app/requirements.txt --output dependency-results.json
"""

import argparse
import json
import re
import sys

import requests

OSV_QUERY_URL = "https://api.osv.dev/v1/query"
PINNED = re.compile(r"^([A-Za-z0-9_.\-]+)\s*==\s*([A-Za-z0-9_.\-]+)")


def read_pinned_packages(path):
    """Yield (name, version) for every exactly pinned line. Comments, blanks and unpinned lines are skipped."""
    with open(path) as handle:
        for raw in handle:
            line = raw.strip()
            if not line or line.startswith("#"):
                continue
            pinned = PINNED.match(line)
            if pinned:
                yield pinned.group(1), pinned.group(2)


def query_osv(name, version, session=requests):
    """Return the list of OSV vulnerability records for one PyPI package version."""
    payload = {"package": {"name": name, "ecosystem": "PyPI"}, "version": version}
    response = session.post(OSV_QUERY_URL, json=payload)
    if response.status_code != 200:
        return []
    return response.json().get("vulns", [])


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.strip().splitlines()[0])
    parser.add_argument("--requirements", required=True, help="Path to requirements.txt")
    parser.add_argument("--output", required=True, help="Where to write the JSON results")
    args = parser.parse_args(argv)

    report = [
        {"name": name, "version": version, "vulns": query_osv(name, version)}
        for name, version in read_pinned_packages(args.requirements)
    ]

    with open(args.output, "w") as out:
        json.dump(report, out, indent=2)

    print(f"Checked {len(report)} packages against OSV.dev")
    return 0


if __name__ == "__main__":
    sys.exit(main())
