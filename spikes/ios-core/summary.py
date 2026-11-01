"""Turns the benchmark reports in an xcodebuild test log into a Markdown table.

Usage: python3 summary.py ios/test.log            (music-core benchmark)
       python3 summary.py --engrave ios/test.log  (Verovio engraving)
"""

import json
import re
import sys


def reports(log):
    for block in re.findall(r"BENCHMARK-BEGIN\n(.*?)\nBENCHMARK-END", log, re.S):
        yield json.loads(block)


def engravings(log):
    for block in re.findall(r"ENGRAVE-BEGIN\n(.*?)\nENGRAVE-END", log, re.S):
        yield json.loads(block)


def engrave_table(path):
    with open(path, encoding="utf-8", errors="replace") as file:
        found = list(engravings(file.read()))
    if not found:
        print("No engraving output.")
        return
    print("| piece | notes | pages | MEI, ms | layout, ms | page 1, ms | all pages, ms | timemap, ms | SVG, KB |")
    print("|---|---:|---:|---:|---:|---:|---:|---:|---:|")
    for e in found:
        print(f"| {e['file']} | {e['notes']} | {e['pages']} | {e['meiMs']:.0f} | {e['layoutMs']:.0f} | "
              f"{e['firstPageMs']:.0f} | {e['allPagesMs']:.0f} | {e['timemapMs']:.0f} | {e['svgKB']} |")
    print()
    print(f"Verovio {found[0]['verovio']}")


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
    if sys.argv[1] == "--engrave":
        engrave_table(sys.argv[2])
    else:
        main(sys.argv[1])
