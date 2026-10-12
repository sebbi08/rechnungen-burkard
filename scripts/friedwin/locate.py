#!/usr/bin/env python3
"""Find where known values sit in the FriedWin files, printing positions only.

Given values read off one invoice (article numbers, amounts) and a string that
identifies the records to look in (the grave's customer number), this lists for
each matching record where each value occurs and in which encoding: as digits, as
packed decimal (COMP-3) with 0-4 implied decimals, or as a binary integer. It
prints file, record, offset and encoding, never the record's content.

    python3 scripts/friedwin/locate.py export/ZWI/<stamp> --anchor 067200 \\
        --values 10300 35.00 150.00
"""

import argparse
import mmap
import os
import sys
from decimal import Decimal

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from schema_profile import records_of  # noqa: E402


def packed(n, sign):
    digits = str(n)
    if len(digits) % 2 == 0:
        digits = "0" + digits
    nibbles = [int(d) for d in digits] + [sign]
    return bytes(nibbles[i] * 16 + nibbles[i + 1] for i in range(0, len(nibbles), 2))


def encodings(value):
    """(label, bytes) for each way a value might be stored."""
    amount = Decimal(value.replace(",", "."))
    out = []
    for scale in range(0, 5):
        n = amount * (10**scale)
        if n != n.to_integral_value() or n <= 0:
            continue
        n = int(n)
        if scale == 0:
            out.append(("digits", str(n).encode()))
        elif len(str(n)) >= 3:
            out.append((f"digits, {scale} decimals", str(n).encode()))
        for sign, name in ((0xC, "C"), (0xF, "F"), (0xD, "D")):
            out.append((f"packed{name}, {scale} decimals", packed(n, sign)))
        if scale <= 2 and n >= 256:
            for width in (2, 4):
                if n < 1 << (8 * width - 1):
                    out.append((f"binary{width}, {scale} decimals", n.to_bytes(width, "big")))
    return out


def find_all(haystack, needle):
    start = 0
    while (i := haystack.find(needle, start)) >= 0:
        yield i
        start = i + 1


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("root")
    parser.add_argument("--anchor", action="append", required=True, help="search records containing this text")
    parser.add_argument("--values", nargs="+", required=True)
    parser.add_argument("--skip-larger-than", type=int, default=100, help="MB")
    args = parser.parse_args()

    anchors = [a.encode() for a in args.anchor]
    wanted = [(v, encodings(v)) for v in args.values]
    paths = []
    for dirpath, _, names in os.walk(args.root):
        paths += [os.path.join(dirpath, n) for n in names if n.lower().endswith(".dat")]

    for path in sorted(paths, key=str.lower):
        if os.path.getsize(path) == 0 or os.path.getsize(path) > args.skip_larger_than * 1e6:
            continue
        with open(path, "rb") as fh:
            data = mmap.mmap(fh.fileno(), 0, access=mmap.ACCESS_READ)
            try:
                _, records = records_of(path, data, 10_000_000)
            finally:
                data.close()
        name = os.path.relpath(path, args.root)
        for index, rec in enumerate(records):
            where = [(a.decode(), i) for a in anchors for i in find_all(rec, a)]
            if not where:
                continue
            print(f"\n{name} record {index + 1} (length {len(rec)})")
            print("   anchor " + ", ".join(f"{a}@{i}" for a, i in where))
            for value, encs in wanted:
                hits = []
                for label, needle in encs:
                    hits += [f"@{i} {label}" for i in find_all(rec, needle)]
                if hits:
                    print(f"   {value:>10}: " + "; ".join(hits[:12]) + (" ..." if len(hits) > 12 else ""))


if __name__ == "__main__":
    main()
