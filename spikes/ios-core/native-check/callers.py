"""Prints the callers of a function in a macOS `sample` report: for its heaviest occurrences in the
call graph, the chain of frames above it, with the number of samples in each.

    python3 callers.py sample.txt <function> [occurrences] [depth]
"""
import re
import sys

path, leaf = sys.argv[1], sys.argv[2]
occurrences = int(sys.argv[3]) if len(sys.argv) > 3 else 3
depth = int(sys.argv[4]) if len(sys.argv) > 4 else 12

frame = re.compile(r"^(?P<indent>[ +!:|]*)(?P<count>\d+) (?P<name>.+?)(?:  \(in (?P<lib>[^)]+)\))?(?: \+ \d+)?(?:  \[.*\])?\s*$")
stack, found = [], []
for line in open(path, encoding="utf-8", errors="replace"):
    if line.startswith("Total number in stack") or line.startswith("Sort by top of stack"):
        break
    match = frame.match(line.rstrip("\n"))
    if not match:
        continue
    level = len(match["indent"])
    entry = (int(match["count"]), match["name"], match["lib"] or "")
    while stack and stack[-1][0] >= level:
        stack.pop()
    stack.append((level, entry))
    if leaf in entry[1]:
        found.append([e for _, e in stack])

for chain in sorted(found, key=lambda c: -c[-1][0])[:occurrences]:
    print(f"--- {chain[-1][0]} samples")
    for count, name, lib in chain[-depth:]:
        print(f"{count:6d}  {name[:110]}  ({lib})")
