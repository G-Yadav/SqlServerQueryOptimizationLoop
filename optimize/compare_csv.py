#!/usr/bin/env python3
"""
Compare two CSV files produced by execute_sp for correctness verification.

Usage: python3 compare_csv.py <golden_path> <candidate_path>

Exit codes:
  0 - files match
  1 - mismatch (row count, column count, or data)
  2 - usage error or file not found
"""

import csv
import math
import sys


MAX_DIFFS_SHOWN = 5


def read_csv(path: str) -> list[list[str]]:
    with open(path, newline="") as f:
        return list(csv.reader(f))


def compare(golden_path: str, candidate_path: str) -> None:
    try:
        golden = read_csv(golden_path)
        candidate = read_csv(candidate_path)
    except FileNotFoundError as e:
        print(f"FILE NOT FOUND: {e}")
        sys.exit(2)

    if len(golden) != len(candidate):
        print(f"ROW COUNT MISMATCH: expected {len(golden)}, got {len(candidate)}")
        sys.exit(1)

    if golden and candidate and len(golden[0]) != len(candidate[0]):
        print(
            f"COLUMN COUNT MISMATCH: expected {len(golden[0])} columns, got {len(candidate[0])}"
        )
        sys.exit(1)

    diffs: list[str] = []
    total_mismatched_rows = 0

    for i, (g_row, c_row) in enumerate(zip(golden, candidate)):
        row_has_diff = False
        for j, (g_val, c_val) in enumerate(zip(g_row, c_row)):
            if g_val == c_val:
                continue
            try:
                if math.isclose(float(g_val), float(c_val), rel_tol=1e-9):
                    continue
            except ValueError:
                pass
            row_has_diff = True
            if len(diffs) < MAX_DIFFS_SHOWN:
                diffs.append(f"  row {i + 1}, col {j + 1}: expected '{g_val}', got '{c_val}'")
        if row_has_diff:
            total_mismatched_rows += 1

    if diffs:
        print(
            f"DATA MISMATCH: {total_mismatched_rows} mismatched row(s)"
            f" (showing first {len(diffs)} difference(s)):"
        )
        print("\n".join(diffs))
        sys.exit(1)

    print(f"OK: {len(golden)} row(s) match")


if __name__ == "__main__":
    if len(sys.argv) != 3:
        print("Usage: compare_csv.py <golden_path> <candidate_path>")
        sys.exit(2)
    compare(sys.argv[1], sys.argv[2])
