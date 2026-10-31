"""Turns the benchmark reports in an xcodebuild test log into a Markdown table.

Usage: python3 summary.py ios/test.log
"""

import json
import re
import sys


def reports(log):
    for block in re.findall(r"BENCHMARK-BEGIN\n(.*?)\nBENCHMARK-END", log, re.S):
        yield json.loads(block)


def main(path):
    with open(path, encoding="utf-8", errors="replace") as file:
        found = list(reports(file.read()))
    if not found:
        print("No benchmark output.")
        return
    for report in found:
        print(f"**{report['file']}**: {report['notes']} notes, {report['bars']} bars, "
              f"{report['phrases']} phrases, {report['facts']} facts; {report['runtime']}")
        print()
        print("| step | first run, ms | median, ms |")
        print("|---|---:|---:|")
        for name, times in report["stagesMs"].items():
            print(f"| {name} | {times['cold']} | {times['median']} |")
        print(f"| **total** | **{report['coldTotalMs']}** | **{report['warmTotalMs']}** |")
        print()


if __name__ == "__main__":
    main(sys.argv[1])
