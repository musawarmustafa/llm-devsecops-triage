"""
Merge the three scanner reports into one CSV ready for manual labelling.

Each row is one finding with Tool, Category, FilePath, Severity and Description.
GroundTruth and Reason are left blank for a human reviewer to fill in.

Usage:
    python build_dataset.py --semgrep semgrep-results.json --gitleaks gitleaks-results.json \
        --dependency dependency-results.json --output labeled_with_groundtruth.csv
"""

import argparse
import csv
import json
import re
import sys
from collections import Counter

CVE_ID = re.compile(r"CVE-\d{4}-\d+")
COLUMNS = ["Tool", "Category", "FilePath", "Severity", "Description", "GroundTruth", "Reason"]
MAX_DESCRIPTION = 400


def load_json(path):
    with open(path, "r", encoding="utf-8") as handle:
        return json.load(handle)


def one_line(text):
    """Flatten a multi-line message to a single trimmed line."""
    return (text or "").replace("\n", " ").strip()


def finding(tool, category, location, severity, description):
    return {"Tool": tool, "Category": category, "FilePath": location,
            "Severity": severity, "Description": description}


def first_cve(aliases):
    return next((alias for alias in aliases if CVE_ID.match(alias)), None)


# ---- SAST (Semgrep) ---------------------------------------------------------

def sast_findings(report):
    """One row per unique (file, message, start line). Semgrep can report the same hit more than once."""
    unique = {}
    for hit in report.get("results", []):
        extra = hit.get("extra", {})
        identity = (hit.get("path", ""), extra.get("message", ""), hit.get("start", {}).get("line"))
        unique.setdefault(identity, finding(
            "semgrep", "SAST", hit.get("path", ""), extra.get("severity", ""), one_line(extra.get("message", ""))))
    return list(unique.values())


# ---- Secrets (Gitleaks) -----------------------------------------------------

def secret_findings(report):
    """Gitleaks has no severity field, so every secret is rated high."""
    return [finding("gitleaks", "Secrets", leak.get("File", ""), "high", one_line(leak.get("Description", "")))
            for leak in (report or [])]


# ---- Dependencies (OSV.dev) -------------------------------------------------

def best_record_per_issue(vulns):
    """
    OSV often returns several records for one issue (e.g. a GHSA and a PYSEC entry for the same CVE).
    Group them by CVE (or by OSV id when there is no CVE) and keep the record with the longest summary.
    """
    chosen = {}
    for record in vulns:
        issue = first_cve(record.get("aliases", [])) or record.get("id")
        current = chosen.get(issue)
        if current is None or len(record.get("summary") or "") > len(current.get("summary") or ""):
            chosen[issue] = record
    return chosen.values()


def dependency_findings(report):
    rows = []
    for package in report:
        location = f"{package.get('name', '')}=={package.get('version', '')}"
        for record in best_record_per_issue(package.get("vulns", [])):
            text = one_line(record.get("summary") or record.get("details") or "")[:MAX_DESCRIPTION]
            label = first_cve(record.get("aliases", [])) or record.get("id", "")
            # Tool is recorded as "pip-audit" to match the labelled dataset used in the study.
            rows.append(finding("pip-audit", "Dependency", location, label, text))
    return rows


# ---- Output -----------------------------------------------------------------

def write_dataset(path, rows):
    with open(path, "w", newline="", encoding="utf-8") as handle:
        writer = csv.DictWriter(handle, fieldnames=COLUMNS)
        writer.writeheader()
        for row in rows:
            writer.writerow({**row, "GroundTruth": "", "Reason": ""})


def main(argv=None):
    parser = argparse.ArgumentParser(description=__doc__.strip().splitlines()[0])
    parser.add_argument("--semgrep", required=True)
    parser.add_argument("--gitleaks", required=True)
    parser.add_argument("--dependency", required=True)
    parser.add_argument("--output", required=True)
    args = parser.parse_args(argv)

    rows = (sast_findings(load_json(args.semgrep))
            + secret_findings(load_json(args.gitleaks))
            + dependency_findings(load_json(args.dependency)))

    write_dataset(args.output, rows)

    print(f"Wrote {len(rows)} rows to {args.output}")
    for category, count in Counter(row["Category"] for row in rows).items():
        print(f"  {category}: {count}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
