#!/usr/bin/env python3
"""Describe the record layout of a FriedWin export without printing any of its data.

FriedWin keeps its data in Micro Focus COBOL files (.DAT with .IDX), and the export
comes without copybooks. This reconstructs the layouts from the files themselves and
prints only structure: record lengths, a per-column character-class map, fill rates,
how many different byte values each column holds, and suggested fields with their
type, fill rate and uniqueness. No field value is printed.

    python3 scripts/friedwin/schema_profile.py export/ZWI/<stamp> > schema.txt

Read schema.txt before passing it on: it should contain nothing but the shapes below.
"""

import argparse
import datetime
import hashlib
import mmap
import os
import sys

HEADER_SIZE = 128

# Character classes, one symbol each.
SPACE, DIGIT, ALPHA, PUNCT, NUL, BIN = "_", "9", "A", ".", "0", "#"

# Umlauts and ß in CP850 (DOS) and Latin-1, both of which old FriedWin data may use.
_UMLAUTS = {0x81, 0x84, 0x8E, 0x94, 0x99, 0x9A, 0xE1, 0xC4, 0xD6, 0xDC, 0xE4, 0xF6, 0xFC, 0xDF}


def _class_of(b):
    if b == 0x20:
        return SPACE
    if 0x30 <= b <= 0x39:
        return DIGIT
    if 0x41 <= b <= 0x5A or 0x61 <= b <= 0x7A or b in _UMLAUTS:
        return ALPHA
    if 0x21 <= b <= 0x7E:
        return PUNCT
    if b == 0:
        return NUL
    return BIN


CLASS_OF = [_class_of(b) for b in range(256)]
# One byte per class, for the fast periodicity search.
CLASS_TABLE = bytes(ord(CLASS_OF[b]) for b in range(256))
# A byte that could be the last byte of a COMP-3 (packed decimal) field.
PACKED_SIGN = [(b >> 4) <= 9 and (b & 0xF) in (0xC, 0xD, 0xF) for b in range(256)]
PACKED_BODY = [(b >> 4) <= 9 and (b & 0xF) <= 9 for b in range(256)]

ORGANIZATIONS = {1: "sequential", 2: "indexed", 3: "relative"}


# --------------------------------------------------------------------------- reading


def mf_header(data):
    """Parse a Micro Focus file header, or return None if the file has none."""
    if len(data) < HEADER_SIZE or data[0] >> 4 != 3:
        return None
    if data[0x24:0x26] != b"\x00\x3e":
        return None
    created = data[0x08:0x16].decode("ascii", "replace")
    return {
        "organization": ORGANIZATIONS.get(data[0x27], f"unknown({data[0x27]})"),
        "compression": data[0x29],
        "idxformat": data[0x2B],
        "recording": "variable" if data[0x30] == 1 else "fixed",
        "max_len": int.from_bytes(data[0x36:0x3A], "big"),
        "min_len": int.from_bytes(data[0x3A:0x3E], "big"),
        "created": created,
    }


def _walk_mf(data, start, hdr_size, align, max_len, limit=None):
    """Yield (type, offset, length) for each record from start, stopping at garbage."""
    pos = start
    n = len(data)
    count = 0
    while pos + hdr_size <= n:
        word = int.from_bytes(data[pos : pos + hdr_size], "big")
        rtype = word >> (hdr_size * 8 - 4)
        length = word & ((1 << (hdr_size * 8 - 4)) - 1)
        if rtype == 0 and length == 0:
            # Zero padding: skip to the next aligned slot.
            pos += max(align, 1)
            continue
        if rtype not in range(1, 9) or length == 0 or length > max(max_len, 1) + 64:
            return
        body = pos + hdr_size
        if body + length > n:
            return
        yield rtype, body, length
        count += 1
        if limit and count >= limit:
            return
        pos = body + length
        if align > 1:
            pos = (pos + align - 1) // align * align


def detect_mf_framing(data, header):
    """Find the record-header size and alignment that parse the start of the file best."""
    best = None
    sample = data[: HEADER_SIZE + 256 * 1024]
    for hdr_size in (2, 4):
        for align in (1, 2, 4, 8):
            parsed = 0
            end = HEADER_SIZE
            for _, body, length in _walk_mf(sample, HEADER_SIZE, hdr_size, align, header["max_len"]):
                parsed += 1
                end = body + length
            covered = end - HEADER_SIZE
            if best is None or covered > best[0]:
                best = (covered, hdr_size, align, parsed)
    covered, hdr_size, align, parsed = best
    usable = len(sample) - HEADER_SIZE
    if usable <= 0 or covered < usable * 0.5 or parsed < 2:
        return None
    return hdr_size, align


def detect_period(data):
    """Guess a fixed record length from how regularly character classes repeat."""
    sample = bytes(data[: 256 * 1024]).translate(CLASS_TABLE)
    if len(sample) < 64:
        return None, 0.0
    max_len = min(4096, len(sample) // 3)
    scores = {}
    for length in range(2, max_len + 1):
        a = int.from_bytes(sample[:-length], "big")
        b = int.from_bytes(sample[length:], "big")
        diff = (a ^ b).to_bytes(len(sample) - length, "big")
        scores[length] = diff.count(0) / len(diff)
    top = max(scores.values())
    for length in sorted(scores):
        if scores[length] >= top * 0.98:
            return length, scores[length]
    return None, 0.0


def records_of(path, data, max_records):
    """Return (description lines, list of record byte strings, extra counts)."""
    info = []
    header = mf_header(data)
    if header:
        info.append(
            "MF header: organization={organization} idxformat={idxformat} "
            "compression={compression} recording={recording} max_len={max_len} "
            "min_len={min_len} created={created}".format(**header)
        )
        if header["compression"]:
            info.append(
                "WARNING: compressed records (routine %d); the class map shows compressed bytes"
                % header["compression"]
            )
        framing = detect_mf_framing(data, header)
        if framing:
            hdr_size, align = framing
            info.append(f"framing: {hdr_size}-byte record headers, {align}-byte alignment")
            types = {}
            records = []
            for rtype, body, length in _walk_mf(data, HEADER_SIZE, hdr_size, align, header["max_len"]):
                types[rtype] = types.get(rtype, 0) + 1
                # 4 normal, 5 reduced, 7/8 user data referenced by a pointer.
                if rtype in (4, 5, 7, 8) and len(records) < max_records:
                    records.append(bytes(data[body : body + length]))
            info.append("record types: " + ", ".join(f"{k}:{v}" for k, v in sorted(types.items())))
            return info, records
        info.append("framing: no record headers found, using fixed stride of max_len")
        stride = header["max_len"]
        body = data[HEADER_SIZE:]
        records = [bytes(body[i : i + stride]) for i in range(0, len(body) - stride + 1, stride)]
        return info, records[:max_records]

    newline_ratio = data[: 1024 * 1024].count(b"\n") / max(1, min(len(data), 1024 * 1024))
    if newline_ratio > 1 / 4096:
        info.append("no MF header; line sequential (split on newlines)")
        lines = bytes(data).split(b"\n")
        records = [line.rstrip(b"\r") for line in lines if line.strip(b"\r\x00 ")]
        return info, records[:max_records]

    length, score = detect_period(data)
    if not length:
        info.append("no MF header; no record structure found")
        return info, []
    remainder = len(data) % length
    info.append(
        f"no MF header; fixed records of {length} bytes guessed from periodicity "
        f"(score {score:.2f}, file size remainder {remainder})"
    )
    records = [bytes(data[i : i + length]) for i in range(0, len(data) - length + 1, length)]
    return info, records[:max_records]


# --------------------------------------------------------------------------- profiling


def column_stats(records):
    width = max(len(r) for r in records)
    counts = [dict.fromkeys(CLASS_OF, 0) for _ in range(width)]
    distinct = [set() for _ in range(width)]
    present = [0] * width
    sign = [0] * width
    packed = [0] * width
    for r in records:
        for i, b in enumerate(r):
            counts[i][CLASS_OF[b]] += 1
            present[i] += 1
            if len(distinct[i]) <= 64:
                distinct[i].add(b)
            sign[i] += PACKED_SIGN[b]
            packed[i] += PACKED_BODY[b]
    cols = []
    for i in range(width):
        n = present[i] or 1
        p = {k: v / n for k, v in counts[i].items()}
        cols.append(
            {
                "p": p,
                "present": present[i] / len(records),
                "distinct": len(distinct[i]),
                "sign": sign[i] / n,
                "packed": packed[i] / n,
            }
        )
    return cols


def column_symbol(col):
    p = col["p"]
    if p[SPACE] > 0.98:
        return SPACE
    if p[NUL] > 0.98:
        return NUL
    filled = 1 - p[SPACE] - p[NUL]
    if filled <= 0:
        return SPACE
    if (p[BIN] + p[NUL]) > 0.3:
        return BIN
    if p[DIGIT] / filled > 0.95:
        return DIGIT
    if (p[ALPHA] + p[PUNCT]) / filled > 0.5:
        return ALPHA
    return "x"  # mixed printable


def fill_digit(col):
    filled = 1 - col["p"][SPACE] - col["p"][NUL]
    return str(min(9, int(filled * 10)))


def distinct_symbol(col):
    d = col["distinct"]
    if d <= 9:
        return str(d)
    if d <= 15:
        return "a"
    if d <= 31:
        return "b"
    return "c"


def suggest_fields(cols):
    """Split columns into fields where the class changes or the fill rate jumps back up."""
    fields = []
    start = 0
    for i in range(1, len(cols) + 1):
        if i < len(cols):
            prev, cur = cols[i - 1], cols[i]
            s_prev, s_cur = column_symbol(prev), column_symbol(cur)
            fill_prev = 1 - prev["p"][SPACE] - prev["p"][NUL]
            fill_cur = 1 - cur["p"][SPACE] - cur["p"][NUL]
            same_kind = s_prev == s_cur or {s_prev, s_cur} <= {ALPHA, "x"} or {s_prev, s_cur} <= {BIN, NUL}
            # Left-justified text thins out towards its end; a jump marks a new field.
            binary = {s_prev, s_cur} <= {BIN, NUL}
            if same_kind and (binary or fill_cur - fill_prev < 0.15):
                continue
        fields.append((start, i - start))
        start = i
    return fields


def _valid_date(text, fmt):
    try:
        datetime.datetime.strptime(text, fmt)
        return True
    except ValueError:
        return False


DATE_FORMATS = {8: ("%Y%m%d", "%d%m%Y"), 6: ("%y%m%d", "%d%m%y")}


def describe_field(records, cols, offset, length):
    values = [r[offset : offset + length] for r in records if len(r) >= offset + length]
    if not values:
        return None
    blank = sum(1 for v in values if not v.strip(b" \x00"))
    used = [v for v in values if v.strip(b" \x00")]
    max_used = max((len(v.rstrip(b" \x00")) for v in used), default=0)
    hashes = {hashlib.blake2b(v, digest_size=8).digest() for v in used}
    symbols = "".join(column_symbol(c) for c in cols[offset : offset + length])
    kind = {DIGIT: "numeric", ALPHA: "text", "x": "mixed", BIN: "binary", SPACE: "unused", NUL: "nulls"}
    main = max(set(symbols), key=symbols.count)
    notes = []
    if used and main == DIGIT and length in DATE_FORMATS:
        for fmt in DATE_FORMATS[length]:
            ok = sum(_valid_date(v.decode("ascii", "replace"), fmt) for v in used) / len(used)
            if ok > 0.9:
                notes.append(f"date {fmt} ({ok:.0%} valid)")
    if used and BIN in symbols and length <= 10:
        last = cols[offset + length - 1]
        body_ok = all(c["packed"] > 0.95 for c in cols[offset : offset + length - 1])
        if last["sign"] > 0.95 and body_ok:
            notes.append(f"packed decimal (COMP-3), {2 * length - 1} digits")
        else:
            notes.append("binary (COMP?)")
    if used and len(hashes) == len(used) and len(used) > 1:
        notes.append("unique")
    if used and len(hashes) <= 12 and len(used) >= 20:
        notes.append(f"code ({len(hashes)} values)")
    return {
        "offset": offset,
        "length": length,
        "kind": kind.get(main, main),
        "fill": 1 - blank / len(values),
        "max_used": max_used,
        "distinct": len(hashes),
        "notes": notes,
    }


def profile_file(path, max_records, out):
    size = os.path.getsize(path)
    out.write(f"\n== {path}  ({size} bytes)\n")
    if size == 0:
        out.write("   empty\n")
        return
    with open(path, "rb") as fh:
        data = mmap.mmap(fh.fileno(), 0, access=mmap.ACCESS_READ)
        try:
            info, records = records_of(path, data, max_records)
        finally:
            data.close()
    for line in info:
        out.write(f"   {line}\n")
    if not records:
        return
    lengths = {}
    for r in records:
        lengths[len(r)] = lengths.get(len(r), 0) + 1
    common = sorted(lengths.items(), key=lambda kv: -kv[1])[:8]
    out.write(f"   records profiled: {len(records)}; lengths: ")
    out.write(", ".join(f"{k}x{v}" for k, v in common))
    out.write(" ..." if len(lengths) > 8 else "")
    out.write("\n")

    cols = column_stats(records)
    out.write("   column map (rows: class / fill 0-9 / distinct bytes 1-9,a<16,b<32,c>=32)\n")
    out.write(f"   classes: {DIGIT} digits, A text, x mixed, {BIN} binary, {SPACE} blank, {NUL} nulls\n")
    for start in range(0, len(cols), 80):
        chunk = cols[start : start + 80]
        out.write(f"   {start:5d}  {''.join(column_symbol(c) for c in chunk)}\n")
        out.write(f"          {''.join(fill_digit(c) for c in chunk)}\n")
        out.write(f"          {''.join(distinct_symbol(c) for c in chunk)}\n")

    out.write("   suggested fields:\n")
    out.write("     offset  len  kind     fill  maxused  distinct  notes\n")
    for offset, length in suggest_fields(cols):
        f = describe_field(records, cols, offset, length)
        if not f or f["kind"] in ("unused", "nulls") and f["fill"] == 0:
            continue
        out.write(
            f"     {f['offset']:6d} {f['length']:4d}  {f['kind']:<7} {f['fill']:5.0%}  "
            f"{f['max_used']:7d}  {f['distinct']:8d}  {'; '.join(f['notes'])}\n"
        )


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("root", help="the export folder, e.g. export/ZWI/20260930-1807")
    parser.add_argument("--max-records", type=int, default=20000, help="records profiled per file")
    parser.add_argument(
        "--skip-larger-than",
        type=int,
        default=100,
        help="skip files larger than this many MB (the archive and the zwi* scratch files)",
    )
    args = parser.parse_args()

    paths = []
    for dirpath, _, names in os.walk(args.root):
        for name in names:
            if name.lower().endswith(".dat"):
                paths.append(os.path.join(dirpath, name))
    for path in sorted(paths, key=str.lower):
        size_mb = os.path.getsize(path) / 1e6
        if size_mb > args.skip_larger_than:
            sys.stdout.write(f"\n== {path}  ({size_mb:.0f} MB) skipped, larger than --skip-larger-than\n")
            continue
        try:
            profile_file(path, args.max_records, sys.stdout)
        except Exception as exc:  # keep going; one odd file should not hide the rest
            sys.stdout.write(f"   ERROR {type(exc).__name__}: {exc}\n")


if __name__ == "__main__":
    main()
