#!/usr/bin/env python3
"""Print a few records of the FriedWin files that matter for the import, with text masked.

The layout found by schema_profile.py tells where the fields are, not which field
links a grave to its customer or its articles, or how prices are encoded. This
prints a handful of records per file, split at the field boundaries, so those links
can be read off — without the personal data:

- Every word is masked letter by letter (Xxxxx) and its digits become 9, unless the
  word occurs in at least --frequent of the file's records (place names, "Herr",
  "Grab" ...). A name that common identifies nobody.
- Numbers and binary fields are shown as they are, except in the customer file,
  where only the customer number and short codes stay visible.

    python3 scripts/friedwin/anonymized_sample.py export/ZWI/<stamp> > sample.txt

Read sample.txt before passing it on.
"""

import argparse
import mmap
import os
import re
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from schema_profile import BIN, NUL, column_stats, column_symbol, records_of  # noqa: E402

# File name (lower case) -> how to show it. "digits": numbers and binary as they are.
# "masked": the customer file, where numbers stay visible only in the key and codes.
FILES = {
    "f01kunr1.dat": "masked",
    "grabsta1.dat": "digits",
    "grabinf1.dat": "digits",
    "artstam1.dat": "digits",
    "pflatex1.dat": "digits",
    "pfletex1.dat": "digits",
    "f31kenz1.dat": "digits",
    "f22anre1.dat": "digits",
    "f19text1.dat": "digits",
    "f24pfle1.dat": "digits",
    "kunmahn1.dat": "digits",
    "muststa1.dat": "digits",
    "fakulnr1.dat": "digits",
    "kuninfo1.dat": "digits",
    "rechein1.dat": "digits",
    "zahlein1.dat": "digits",
    "r0320251.dat": "digits",
}

# Customer file: the customer number, shown so graves and invoices can be matched to it.
CUSTOMER_KEY = range(3, 14)

# Digits and letters are separate words: fields often touch without a blank between them.
WORD = re.compile(rb"[0-9]+|[A-Za-z\x81\x84\x8e\x94\x99\x9a\xe1\xc4\xd6\xdc\xe4\xf6\xfc\xdf]+")


def mask_word(word):
    out = bytearray()
    for b in word:
        if 0x30 <= b <= 0x39:
            out += b"9"
        elif 0x61 <= b <= 0x7A or b in (0x81, 0x84, 0x94, 0xE1, 0xE4, 0xF6, 0xFC, 0xDF):
            out += b"x"
        else:
            out += b"X"
    return bytes(out)


def frequent_words(records, threshold):
    """Words, and numbers at a given offset, that occur in at least threshold records."""
    seen = {}
    for r in records:
        tokens = set()
        for m in WORD.finditer(r):
            w = m.group()
            tokens.add((m.start(), w) if w.isdigit() else w.lower())
        for t in tokens:
            seen[t] = seen.get(t, 0) + 1
    return {t for t, n in seen.items() if n >= threshold}


def regions(cols):
    """Split the columns into runs that are either binary or printable."""
    out = []
    start = 0
    for i in range(1, len(cols) + 1):
        if i < len(cols):
            a = column_symbol(cols[i - 1]) in (BIN, NUL)
            b = column_symbol(cols[i]) in (BIN, NUL)
            if a == b:
                continue
        out.append((start, i, column_symbol(cols[start]) in (BIN, NUL)))
        start = i
    return out


def show_text(chunk, offset, policy, allowed):
    """Yield (offset, text) for each run of non-blank text, separated by 2+ blanks."""
    for m in re.finditer(rb"\S+(?: \S+)*", chunk):
        seg = m.group()
        at = offset + m.start()

        def replace(w):
            word = w.group()
            if word.isdigit():
                start = at + w.start()
                keep = (
                    policy == "digits"
                    or (start, word) in allowed
                    or all(c in CUSTOMER_KEY for c in range(start, start + len(word)))
                )
                return word if keep else mask_word(word)
            if word.lower() in allowed:
                return word
            return mask_word(word)

        yield at, WORD.sub(replace, seg).decode("cp850")


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("root", help="the export folder, e.g. export/ZWI/20260930-1807")
    parser.add_argument("--records", type=int, default=8, help="records shown per file")
    parser.add_argument(
        "--frequent",
        type=float,
        default=0.02,
        help="a word stays readable if it occurs in at least this share of records (and in 25)",
    )
    args = parser.parse_args()

    paths = {}
    for dirpath, _, names in os.walk(args.root):
        for name in names:
            if name.lower() in FILES and "frieddat" in dirpath.lower():
                paths[name.lower()] = os.path.join(dirpath, name)

    for key, policy in FILES.items():
        path = paths.get(key)
        if not path or os.path.getsize(path) == 0:
            continue
        with open(path, "rb") as fh:
            data = mmap.mmap(fh.fileno(), 0, access=mmap.ACCESS_READ)
            try:
                _, records = records_of(path, data, 1_000_000)
            finally:
                data.close()
        if not records:
            continue
        cols = column_stats(records[:20000])
        allowed = frequent_words(records, max(25, args.frequent * len(records)))
        count = min(args.records, len(records))
        picks = sorted({round(i * (len(records) - 1) / max(1, count - 1)) for i in range(count)})

        print(f"\n== {os.path.basename(path)}  ({len(records)} records, policy {policy})")
        print(f"   words left readable: {len(allowed)}")
        for n in picks:
            rec = records[n]
            print(f"-- record {n + 1}")
            for start, end, binary in regions(cols):
                chunk = rec[start:end]
                if not chunk.strip(b" \x00"):
                    continue
                if binary:
                    if policy == "digits":
                        print(f"   @{start:<5} hex {chunk.hex(' ')}")
                    else:
                        print(f"   @{start:<5} binary, {len(chunk)} bytes, masked")
                    continue
                for at, text in show_text(chunk, start, policy, allowed):
                    print(f"   @{at:<5} {text}")


if __name__ == "__main__":
    main()
