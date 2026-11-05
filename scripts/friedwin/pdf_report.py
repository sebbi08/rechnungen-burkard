"""A small PDF writer for the conversion report: headings, wrapped text, tables.

It needs nothing beyond the standard library, like the rest of these scripts,
and so uses the PDF's built-in Helvetica in the Windows-1252 encoding, which
covers German text, "€" and "–". Text is wrapped by Helvetica's own widths.
"""

import datetime

PAGE_WIDTH, PAGE_HEIGHT = 595.28, 841.89  # A4
MARGIN = 56.0
BOTTOM = 60.0  # the lowest a line of content may sit; the footer goes below

# Helvetica's advance widths (per 1000 units of the font size) for ASCII 32-126;
# every other character is taken as wide as a digit, which holds for the
# umlauts, "ß" is 611, close enough.
_ASCII_WIDTHS = [
    278, 278, 355, 556, 556, 889, 667, 191, 333, 333, 389, 584, 278, 333, 278, 278,
    556, 556, 556, 556, 556, 556, 556, 556, 556, 556, 278, 278, 584, 584, 584, 556,
    1015, 667, 667, 722, 722, 667, 611, 778, 722, 278, 500, 667, 556, 833, 722, 778,
    667, 778, 722, 667, 611, 722, 667, 944, 667, 667, 611, 278, 278, 278, 469, 556,
    333, 556, 556, 500, 556, 556, 278, 556, 556, 222, 222, 500, 222, 833, 556, 556,
    556, 556, 333, 500, 278, 556, 500, 722, 500, 500, 500, 334, 260, 334, 584,
]
_WIDTHS = {chr(32 + i): w for i, w in enumerate(_ASCII_WIDTHS)}
# Helvetica-Bold runs wider; its widths are taken as the regular ones scaled up.
_BOLD = 1.1


def text_width(text, size, bold=False):
    units = sum(_WIDTHS.get(ch, 556) for ch in text)
    return units * size / 1000 * (_BOLD if bold else 1)


def wrap(text, width, size, bold=False):
    """`text` broken into lines no wider than `width`; a word wider than that stands alone."""
    lines = []
    for paragraph in text.split("\n"):
        line = ""
        for word in paragraph.split():
            candidate = f"{line} {word}" if line else word
            if line and text_width(candidate, size, bold) > width:
                lines.append(line)
                line = word
            else:
                line = candidate
        lines.append(line)
    return lines


def _escape(text):
    return text.replace("\\", "\\\\").replace("(", "\\(").replace(")", "\\)")


class Document:
    """Flows content down A4 pages, starting a new page when one is full."""

    def __init__(self, title):
        self.title = title
        self.pages = []
        self._new_page()

    # ------------------------------------------------------------------ drawing

    def _new_page(self):
        self.ops = []
        self.pages.append(self.ops)
        self.y = PAGE_HEIGHT - MARGIN

    def _ensure(self, height):
        """Starts a new page unless `height` more fits on this one."""
        if self.y - height < BOTTOM:
            self._new_page()
            return True
        return False

    def _text(self, x, y, text, size, bold=False, gray=0.0):
        font = "F2" if bold else "F1"
        self.ops.append(f"{gray:.2f} g BT /{font} {size:.1f} Tf {x:.2f} {y:.2f} Td ({_escape(text)}) Tj ET")

    def _rule(self, x1, y, x2, gray=0.75, width=0.5):
        self.ops.append(f"{gray:.2f} G {width:.2f} w {x1:.2f} {y:.2f} m {x2:.2f} {y:.2f} l S")

    def _fill(self, x, y, w, h, gray):
        self.ops.append(f"{gray:.2f} g {x:.2f} {y:.2f} {w:.2f} {h:.2f} re f")

    # ------------------------------------------------------------------ content

    @property
    def width(self):
        return PAGE_WIDTH - 2 * MARGIN

    def heading(self, text, level=1):
        size = {1: 18.0, 2: 13.0, 3: 11.0}[level]
        before = {1: 0.0, 2: 18.0, 3: 12.0}[level]
        lines = wrap(text, self.width, size, bold=True)
        # a heading never ends a page: it keeps the start of what follows with it
        if not self._ensure(before + len(lines) * size * 1.25 + 40):
            self.y -= before
        for line in lines:
            self.y -= size * 1.25
            self._text(MARGIN, self.y, line, size, bold=True)
        self.y -= size * 0.5

    def paragraph(self, text, size=10.0, gray=0.0, after=6.0):
        leading = size * 1.35
        for line in wrap(text, self.width, size):
            self._ensure(leading)
            self.y -= leading
            self._text(MARGIN, self.y, line, size, gray=gray)
        self.y -= after

    def spacer(self, height):
        self.y -= height

    def page_break(self):
        """Starts a new page, unless this one is still empty."""
        if self.ops:
            self._new_page()

    def table(self, header, rows, widths, align=None, size=9.5, emphasise_last=False):
        """Rows of cells under a header that is repeated on every page the table spans.

        `widths` are fractions of the text width; `align` holds "l" or "r" per column.
        With `emphasise_last` the last row is set bold over a rule, as a total.
        """
        align = align or ["l"] * len(header)
        widths = [w * self.width for w in widths]
        pad, leading = 4.0, size * 1.3

        def draw_row(cells, bold=False, shade=None):
            wrapped = [wrap(c, w - 2 * pad, size, bold) for c, w in zip(cells, widths)]
            height = max(len(w) for w in wrapped) * leading + 2 * pad
            if shade is not None:
                self._fill(MARGIN, self.y - height, self.width, height, shade)
            x = MARGIN
            for lines, w, a in zip(wrapped, widths, align):
                y = self.y - pad
                for line in lines:
                    y -= leading
                    tx = x + w - pad - text_width(line, size, bold) if a == "r" else x + pad
                    self._text(tx, y + leading * 0.25, line, size, bold)
                x += w
            self.y -= height
            return height

        def row_height(cells, bold=False):
            return max(len(wrap(c, w - 2 * pad, size, bold)) for c, w in zip(cells, widths)) * leading + 2 * pad

        def draw_header():
            draw_row(header, bold=True, shade=0.92)
            self._rule(MARGIN, self.y, MARGIN + self.width, gray=0.55)

        self._ensure(row_height(header, True) + row_height(rows[0] if rows else header))
        draw_header()
        for i, cells in enumerate(rows):
            total = emphasise_last and i == len(rows) - 1
            if self._ensure(row_height(cells, total)):
                draw_header()
            if total:
                self._rule(MARGIN, self.y, MARGIN + self.width, gray=0.3, width=0.8)
            draw_row(cells, bold=total)
            self._rule(MARGIN, self.y, MARGIN + self.width, gray=0.85)
        self.y -= 8

    def numbers(self, numbers, size=9.5, column=58.0):
        """Grave or item numbers in even columns across the page."""
        per_line = max(1, int(self.width // column))
        leading = size * 1.4
        for start in range(0, len(numbers), per_line):
            self._ensure(leading)
            self.y -= leading
            for i, number in enumerate(numbers[start : start + per_line]):
                self._text(MARGIN + i * column, self.y, number, size)
        self.y -= 8

    # ------------------------------------------------------------------ output

    def save(self, path):
        objects = []

        def add(body):
            objects.append(body)
            return len(objects)

        catalog, pages = add(b""), add(b"")
        regular = add(b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>")
        bold = add(b"<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding /WinAnsiEncoding >>")
        kids = []
        count = len(self.pages)
        for number, ops in enumerate(self.pages, 1):
            footer = [
                f"0.45 g BT /F1 8.0 Tf {MARGIN:.2f} 32.00 Td ({_escape(self.title)}) Tj ET",
                f"0.45 g BT /F1 8.0 Tf {PAGE_WIDTH - MARGIN - text_width(f'Seite {number} von {count}', 8):.2f} "
                f"32.00 Td (Seite {number} von {count}) Tj ET",
            ]
            stream = "\n".join(ops + footer).encode("cp1252", "replace")
            content = add(b"<< /Length %d >>\nstream\n" % len(stream) + stream + b"\nendstream")
            kids.append(
                add(
                    (
                        f"<< /Type /Page /Parent {pages} 0 R /MediaBox [0 0 {PAGE_WIDTH} {PAGE_HEIGHT}] "
                        f"/Resources << /Font << /F1 {regular} 0 R /F2 {bold} 0 R >> >> /Contents {content} 0 R >>"
                    ).encode()
                )
            )
        objects[catalog - 1] = f"<< /Type /Catalog /Pages {pages} 0 R >>".encode()
        objects[pages - 1] = (
            f"<< /Type /Pages /Kids [{' '.join(f'{k} 0 R' for k in kids)}] /Count {count} >>".encode()
        )
        stamp = datetime.datetime.now().strftime("D:%Y%m%d%H%M%S")
        info = add(f"<< /Title ({_escape(self.title)}) /CreationDate ({stamp}) >>".encode("cp1252", "replace"))

        out = bytearray(b"%PDF-1.4\n%\xe2\xe3\xcf\xd3\n")
        offsets = []
        for number, body in enumerate(objects, 1):
            offsets.append(len(out))
            out += b"%d 0 obj\n" % number + body + b"\nendobj\n"
        xref = len(out)
        out += b"xref\n0 %d\n0000000000 65535 f \n" % (len(objects) + 1)
        for offset in offsets:
            out += b"%010d 00000 n \n" % offset
        out += b"trailer\n<< /Size %d /Root %d 0 R /Info %d 0 R >>\nstartxref\n%d\n%%%%EOF\n" % (
            len(objects) + 1,
            catalog,
            info,
            xref,
        )
        with open(path, "wb") as fh:
            fh.write(out)
