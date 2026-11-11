#!/usr/bin/env python3
"""Turn a FriedWin export into an import archive for the invoicing app.

    python3 scripts/friedwin/convert.py export/ZWI/<stamp> --out friedwin-import.zip

Where the data comes from:

- recipients.csv: every customer in frieddat/F01KUNR1.DAT. In FriedWin the
  customer number is the grave's running number, so a grave and its payer share it.
- graves.csv: every grave in frieddat/GRABSTA1.DAT, with the text printed after
  "GRABSTÄTTE:" on its last care invoice as the site name, field number and
  position from the grave's location text, and its care term.
- items.csv and mapping.csv: FriedWin keeps no list of what a grave gets. Its
  invoices do: frieddat/ARCHIVX1.DAT holds the print data of every invoice ever
  issued. A grave's items are read off its last care invoice of each half-year:
  the "Grabpflege" line as the all-year item, and the lines under "Fruehjahr",
  "Sommer", "Herbst" and "Winter" as the items of that quarter.

The report on stdout names graves by number only. It lists what could not be
converted and what was guessed, so it can be fixed in FriedWin or in the app.
The same goes, in German and with the grave counts added up, into a PDF.
"""

import argparse
import collections
import csv
import datetime
import io
import mmap
import os
import re
import sys
import zipfile
from dataclasses import dataclass, field
from decimal import Decimal, InvalidOperation

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from pdf_report import Document  # noqa: E402
from schema_profile import records_of  # noqa: E402

ENCODING = "latin-1"
ARCHIVE_RECORD = 2307
ARCHIVE_FIELDS = 92

# The item FriedWin bills the care itself under since 2026; older invoices carry
# the same line without a number.
CARE_ITEM = "99990"
CARE_DESCRIPTION = "Grabpflege"

SEASONS = {
    "fruehjahr": "Spring",
    "frühjahr": "Spring",
    "sommer": "Summer",
    "herbst": "Autumn",
    "winter": "Winter",
}
# The reduced rates of the second half of 2020, billed at today's rates.
VAT_NOW = {Decimal(16): Decimal(19), Decimal(5): Decimal(7)}
# A grave record names its cemetery by a code into FriedWin's cemetery table
# (f24pfle1.DAT), and the invoices print it after the grave's name ("GRABSTÄTTE:
# Name  Speyer"), spelled differently over the years. The app's name for each
# spelling, in the table and on the invoices:
CEMETERIES = {
    "FRIEDHOF SPEYER": "Speyer",
    "Speyer": "Speyer",
    "FRIEDHOF DUDENH": "Dudenhofen",
    "Harthausen": "Harthausen",
    "Otterstadt": "Otterstadt",
}
CEMETERY_SUFFIX = re.compile(
    r"\s+(" + "|".join(re.escape(c) for c in sorted(CEMETERIES, key=len, reverse=True)) + r")$"
)
# Suffix of an item number when one FriedWin article is billed in several quarters.
QUARTER_SUFFIX = {"Spring": "F", "Summer": "S", "Autumn": "H", "Winter": "W"}

# Why a grave stays out of the archive: the text report's line, and the PDF
# report's heading and explanation.
LEFT_OUT = {
    "never-billed": (
        "never billed for care, see the separate report (left out)",
        "Nie für Grabpflege abgerechnet",
        "FriedWin hat für diese beendeten Gräber nie eine Rechnung mit einer Grabpflege-Zeile gedruckt. "
        "Was ein Grab bekommt, steht in FriedWin nur auf seinen Rechnungen; ohne eine solche Rechnung "
        "gibt es nichts, woraus sich die Leistungen des Grabes ablesen ließen. Mit Grabstätte und Kunde "
        "stehen die Gräber, zusammen mit den aktiven, die als Genossenschaftsgräber übernommen sind, in "
        "der Datei {csv}.",
    ),
    "no-site": (
        "no site name (left out)",
        "Kein Name der Grabstätte",
        "Weder die letzte Pflege-Rechnung („GRABSTÄTTE: …“) noch der Grabsatz nennt einen Namen. Die "
        "App verlangt ihn.",
    ),
    "no-cemetery": (
        "cemetery code of the grave record not in the cemetery table (left out)",
        "Kein Friedhof erkennbar",
        "Der Grabsatz nennt keinen Friedhof aus FriedWins Friedhofstabelle, oder der Friedhof ist dem "
        "Umwandlungsskript unbekannt. Die App verlangt einen Friedhof.",
    ),
    "ends-before-start": (
        "care ends before it starts (left out)",
        "Pflege endet vor ihrem Beginn",
        "Das Pflegeende liegt vor dem Pflegebeginn. Solche Daten nimmt die App nicht an.",
    ),
    "no-vat": (
        "VAT rate of a line not found (left out)",
        "Steuersatz nicht ermittelbar",
        "Eine Zeile der letzten Pflege-Rechnung eines Halbjahres trägt ein Steuerkennzeichen, zu dem "
        "die Rechnung keinen Steuersatz ausweist.",
    ),
    "before-euro": (
        "last care invoice before the euro (left out)",
        "Letzte Pflege-Rechnung vor dem Euro",
        "Die jüngste Pflege-Rechnung des Grabes stammt aus der Zeit vor 2002. Ihre Preise sind "
        "DM-Preise und taugen nicht für die Leistungen des Grabes.",
    ),
    "no-customer": (
        "customer record missing (left out)",
        "Kein Empfänger",
        "Zum Grab gibt es keinen brauchbaren Kundensatz, und die Anschrift der letzten Rechnung reicht "
        "für keinen Empfänger (ein Name und mindestens eine Anschriftzeile).",
    ),
}

# What was guessed or filled in for a grave that is imported, likewise.
NOTES = {
    "cooperative": (
        "active grave with a contract number and no care invoice in the export's year, imported as a "
        "cooperative grave",
        "Als Genossenschaftsgrab übernommen",
        "Der Grabsatz ist aktiv, nennt eine Vertragsnummer, und FriedWin hat für das Grab im Jahr des "
        "Exports keine Pflege-Rechnung gedruckt. Das Grab ist als Genossenschaftsgrab im Importarchiv, "
        "mit seiner Vertragsnummer. Hat FriedWin das Grab früher abgerechnet, behält es seine "
        "Leistungen und seinen Empfänger; sonst kommen die Angaben des Grabsatzes, der Kundensatz als "
        "Empfänger, wenn es einen brauchbaren gibt, und die Grabpflege zum Pflegepreis des Grabsatzes.",
    ),
    "incomplete": (
        "ordinary grave never billed by FriedWin, imported without a recipient and/or without a care item",
        "Ohne Empfänger oder Leistung übernommen",
        "Der Grabsatz ist aktiv, hat keine Vertragsnummer oder eine Pflege-Rechnung aus dem Jahr des "
        "Exports, und FriedWin hat das Grab nie (oder nur vor dem Euro) abgerechnet. Das Grab ist als "
        "normales Grab im Importarchiv, aber ohne Empfänger (kein brauchbarer Kundensatz) und/oder ohne "
        "Leistung (kein Pflegepreis im Grabsatz). Beides ist in der App nachzutragen, bevor das Grab "
        "berechnet wird.",
    ),
    "empty-record": (
        "grave record without a name, site name written as '-'",
        "Leerer Grabsatz",
        "Der Grabsatz nennt keinen Namen der Grabstätte; übernommen ist „-“ als Name.",
    ),
    "no-start": (
        "no care start, first care invoice's month taken (cooperative graves: January of the export's year)",
        "Pflegebeginn ergänzt",
        "Der Grabsatz nennt keinen Pflegebeginn; übernommen ist der Monat der ersten Pflege-Rechnung, bei "
        "einem Genossenschaftsgrab der Januar des Exportjahres.",
    ),
    "price-differs": (
        "care price of grave record and last invoice differ by more than 10 % (grave record taken)",
        "Pflegepreis weicht von der letzten Rechnung ab",
        "Grabsatz und letzte Pflege-Rechnung nennen Pflegepreise, die mehr als 10 % auseinanderliegen. "
        "Übernommen ist der Preis des Grabsatzes, den FriedWin als Nächstes berechnet hätte.",
    ),
    "cemetery-differs": (
        "the last invoice naming a cemetery names another than the grave record (grave record taken)",
        "Friedhof der Rechnungen weicht ab",
        "Die letzte Rechnung, die einen Friedhof nennt, nennt einen anderen als der Grabsatz. "
        "Übernommen ist der Friedhof des Grabsatzes.",
    ),
    "position-unknown": (
        "grave kind not recognised in the location text (position Unknown...)",
        "Grabart nicht erkannt",
        "Der Lagetext nennt keine erkennbare Grabart; übernommen ist die Lage „unbekannt“ (mit Seite, "
        "wenn eine genannt ist). Mit --positions lässt sie sich je Grab vorgeben.",
    ),
    "no-field": (
        "no field number (written as '-')",
        "Keine Feldnummer",
        "Der Lagetext nennt keine Feldnummer; übernommen ist „-“.",
    ),
    "invoice-address": (
        "no usable customer record, address of last invoice taken",
        "Empfänger aus der Rechnungsanschrift",
        "Es gibt keinen brauchbaren Kundensatz; der Empfänger ist aus der Anschrift der letzten "
        "Rechnung gebildet.",
    ),
    "shares": (
        "billed in shares to several payers in FriedWin, imported with full prices and one recipient "
        "(add the others in the app)",
        "Auf mehrere Zahler aufgeteilt",
        "FriedWin berechnet das Grab anteilig mehreren Zahlern. Übernommen sind die vollen Preise und "
        "ein Empfänger; die übrigen Empfänger sind in der App zu ergänzen.",
    ),
    "duplicate": (
        "grave number occurs more than once, numbered on with -2, -3 ...",
        "Grabnummer mehrfach vergeben",
        "FriedWin führt die Grabnummer mehr als einmal; ab dem zweiten Grab ist ihr „-2“, „-3“ … "
        "angehängt.",
    ),
}


# --------------------------------------------------------------------------- helpers


def text(raw):
    return raw.decode(ENCODING).replace("\0", " ").strip()


def unpack(raw):
    """A COMP-3 field as a digit string, or None if it is not packed decimal."""
    nibbles = []
    for b in raw:
        nibbles += [b >> 4, b & 0xF]
    *digits, sign = nibbles
    if any(d > 9 for d in digits) or sign not in (0xC, 0xD, 0xF):
        return None
    return "".join(map(str, digits))


def month(raw):
    """A packed MMYYYY field as "YYYY-MM", or None for 0 or nonsense."""
    digits = unpack(raw)
    if not digits or int(digits) == 0:
        return None
    mm, yyyy = int(digits[-6:-4]), int(digits[-4:])
    if not 1 <= mm <= 12 or not 1900 <= yyyy <= 2100:
        return None
    return f"{yyyy:04d}-{mm:02d}"


def amount(raw):
    """"+00150,00" -> Decimal("150.00"); blank -> None."""
    raw = raw.strip().replace(".", "").replace(",", ".")
    if not raw:
        return None
    try:
        return Decimal(raw)
    except InvalidOperation:
        return None


def money(value):
    return f"{value.quantize(Decimal('0.01'))}"


def euro(value):
    """Decimal("1234.5") -> "1.234,50 €", as the PDF report shows amounts."""
    english = f"{value.quantize(Decimal('0.01')):,}"
    return english.replace(",", " ").replace(".", ",").replace(" ", ".") + " €"


def shown_month(value):
    """"2026-03" -> "03/2026", as the app shows a care start or end; None -> "–"."""
    return f"{value[5:7]}/{value[:4]}" if value else "–"


def read(root, name):
    return read_file(os.path.join(root, "frieddat", name))


def read_file(path):
    with open(path, "rb") as fh:
        data = mmap.mmap(fh.fileno(), 0, access=mmap.ACCESS_READ)
        try:
            return records_of(path, data, 10_000_000)[1]
        finally:
            data.close()


# --------------------------------------------------------------------------- master data


@dataclass
class Customer:
    number: str
    name: str
    salutation: str
    address: list
    email: str


# A customer record ends its contact data in eight slots of a two-digit kind and
# a hundred characters: 07 phone, 04 a second number, 08 e-mail.
CONTACT_SLOTS = range(780, 780 + 8 * 102, 102)
EMAIL_KIND = b"08"
EMAIL = re.compile(r"[^\s@;,]+@[^\s@;,]+\.[A-Za-z]{2,}")


def email_of(r):
    """The customer's e-mail address: the e-mail slot, else one typed into a phone field."""
    for slot in CONTACT_SLOTS:
        if r[slot : slot + 2] == EMAIL_KIND and (m := EMAIL.search(text(r[slot + 2 : slot + 102]))):
            return m.group()
    for start, end in [(678, 780)] + [(slot + 2, slot + 102) for slot in CONTACT_SLOTS]:
        if m := EMAIL.search(text(r[start:end])):
            return m.group()
    return ""


def read_customers(root):
    salutations = {r[0:2].decode(): text(r[2:24]) for r in read(root, "f22anre1.DAT")}
    customers = {}
    for r in read(root, "F01KUNR1.DAT"):
        number = r[3:9].decode()
        # the country code ("CH", "NL") in front of the postcode, as FriedWin prints it
        country, plz, place = text(r[218:221]), text(r[221:226]), text(r[226:266])
        town = " ".join(x for x in (country, plz, place) if x)
        lines = [text(r[54:94]), text(r[94:134]), text(r[178:218]), town]
        customers[number] = Customer(
            number=number,
            name=text(r[14:54]),
            salutation=salutations.get(r[134:136].decode(), ""),
            address=[line for line in lines if line],
            email=email_of(r),
        )
    return customers


@dataclass
class Grave:
    number: str
    customer: str
    cemetery: str
    site: str
    location: str
    start: str
    end: str
    care_price: Decimal
    contract: str


def read_cemeteries(root):
    """The app's cemetery for each code of FriedWin's cemetery table; unknown names are left out."""
    table = {r[3:5].decode(): text(r[16:56]) for r in read(root, "f24pfle1.DAT")}
    return {code: CEMETERIES[name] for code, name in table.items() if name in CEMETERIES}


def read_graves(root):
    graves = []
    for r in read(root, "GRABSTA1.DAT"):
        care = unpack(r[287:291])
        graves.append(
            Grave(
                number=r[5:11].decode(),
                customer=r[5:11].decode(),
                cemetery=r[3:5].decode(),
                site=text(r[72:96]) or text(r[32:56]),
                location=re.sub(r"\s+", " ", text(r[104:160])),
                start=month(r[236:240]),
                end=month(r[240:244]),
                care_price=Decimal(care) / 100 if care else None,
                contract=text(r[250:261]),
            )
        )
    return graves


# Grave kinds as FriedWin's hand-typed location texts spell them: "27 dGr.a",
# "14 eGr i", "56eGr.a.", "Ur.Gr.i", "60 Urne innen", "4fGr.a", "Rasengrab".
# The first pattern that matches decides the kind, so the more specific come
# first ("Rasen urne" before "Rasen" and "Urne"). The app's position is the kind
# followed by the side, or the kind alone when the text names no side.
URN = r"(?:ur(?:ne|nen)?n?\.?\s*(?:gr(?:ab)?\.?|g(?![a-z]))?(?:\s*st\.)?|ugr\.?)"
KIND_PATTERNS = [
    ("Quadruple", r"(?<![0-9])4\s*f(?:ach)?(?:\s*gr(?:ab)?[a-z]?)?\.?"),
    ("Triple", r"(?<![0-9])3\s*f(?:ach)?(?:\s*gr(?:ab)?[a-z]?)?\.?"),
    ("LawnUrn", r"ras(?:en)?\.?\s*" + URN + r"|" + URN + r"\s*ras(?:en)?\.?"),
    ("Lawn", r"rasen\w*(?:\s*e\.?(?=\s*[ia]\b))?|grüngrab"),
    ("Urn", URN),
    ("Double", r"(?<![a-z])(?:d|doppel)\.?\s*gr[a-z]?\.?"),
    ("Single", r"(?<![a-z])(?:e|einzel)\.?\s*gr[a-z]?\.?|(?<![a-z])e\.(?=\s*[ia]\b)"),
    ("Child", r"(?<![a-z])k\.?\s*gr\.?"),
    ("Tree", r"baum\w*"),
    ("Honorary", r"ehren\w*"),
    ("Slab", r"platten\w*"),
    ("Unknown", r"(?<![a-z])gr(?:ab)?\.?"),
]
SIDE_AFTER = re.compile(r"^\s*\.?\s*(innen|aussen|außen|au|i|a|1)(?![a-z])\.?")
SIDE_ANYWHERE = re.compile(r"(?<![a-z])(innen|aussen|außen|i|a)(?![a-z])\.?")
SIDES = {"i": "Inside", "innen": "Inside", "1": "Inside", "a": "Outside", "au": "Outside",
         "aussen": "Outside", "außen": "Outside"}
def parse_location(location):
    """(field number, kind, side) read off a location text; kind and side may be None."""
    s = location.lower()
    kind, span = None, None
    for name, pattern in KIND_PATTERNS:
        m = re.search(pattern, s)
        if m:
            kind, span = name, m.span()
            break
    side = None
    if span:
        after = SIDE_AFTER.match(s[span[1] :])
        if after:
            side = SIDES[after.group(1)]
            span = (span[0], span[1] + after.end())
    rest = location[: span[0]] + " " + location[span[1] :] if span else location
    if side is None:
        m = SIDE_ANYWHERE.search(rest.lower())
        if m:
            side = SIDES[m.group(1)]
            rest = rest[: m.start()] + " " + rest[m.end() :]
    field_number = re.sub(r"\s+", " ", rest).strip(" .,")
    return field_number, kind, side


def position_of(kind, side):
    """The app's position: "DoubleOutside", "Lawn" (no side named), "Unknown" (nothing)."""
    return (kind or "Unknown") + (side or "")


# --------------------------------------------------------------------------- invoices


@dataclass
class Line:
    article: str
    text: str
    quantity: Decimal
    unit: str
    price: Decimal
    total: Decimal
    vat_code: str
    quarter: str


@dataclass
class Invoice:
    number: str
    customer: str
    date: datetime.date
    site: str
    address: tuple
    lines: list = field(default_factory=list)
    rates: dict = field(default_factory=dict)

    @property
    def half(self):
        return (self.date.year, 1 if self.date.month <= 6 else 2)

    @property
    def is_care(self):
        return any(is_care_line(line) for line in self.lines)


def is_care_line(line):
    # Since 2026 the care is billed as article 99990, which FriedWin also uses for
    # other free text ("Kosten für Mehrfachrechnung"): only the text tells them apart.
    return line.text.startswith("Grabpflege")


def read_invoices(root, customers):
    """The care invoices of the given customers, by customer, from the print archive."""
    path = os.path.join(root, "frieddat", "ARCHIVX1.DAT")
    invoices = collections.defaultdict(dict)
    with open(path, "rb") as fh:
        data = mmap.mmap(fh.fileno(), 0, access=mmap.ACCESS_READ)
        current, quarter = None, None
        for r in range(len(data) // ARCHIVE_RECORD):
            raw = data[r * ARCHIVE_RECORD : (r + 1) * ARCHIVE_RECORD]
            f = [v.replace("\0", " ").strip() for v in raw.decode(ENCODING).split(";")]
            if len(f) < ARCHIVE_FIELDS:
                continue
            if f[1].startswith("****"):
                quarter = None
                current = None
                if f[8] not in customers:
                    continue
                try:
                    date = datetime.datetime.strptime(f[9], "%d.%m.%Y").date()
                except ValueError:
                    continue
                # A second page repeats the header of the same invoice.
                current = invoices[f[8]].setdefault(
                    f[7],
                    Invoice(f[7], f[8], date, f[21].partition(":")[2].strip(), tuple(f[84:92])),
                )
                continue
            if current is None:
                continue
            if f[23].lower() in SEASONS and not f[22]:
                quarter = SEASONS[f[23].lower()]
            elif f[22]:
                current.lines.append(
                    Line(
                        article=f[20],
                        text=f[22],
                        quantity=amount(f[26]),
                        unit=f[28],
                        price=amount(f[27]) or Decimal(0),
                        total=amount(f[30]) or Decimal(0),
                        vat_code=f[31].lstrip("+"),
                        quarter=quarter,
                    )
                )
            if (amount(f[48]) or 0) > 0:
                current.rates = {f[35]: amount(f[48]), f[36]: amount(f[49])}
        data.close()
    return {c: [i for i in invs.values() if i.is_care] for c, invs in invoices.items()}


CENT = Decimal("0.01")


def care_terms(line):
    """(count, price of a full half-year, share billed) of a care line.

    "6 Mon. 150,00" and "12 Mon. 155,00" carry the half-year's price as the unit
    price, while the total may be prorated or cover the year. "1,000 113,00 56,50"
    bills a share of a grave that several payers split.
    """
    q = line.quantity or Decimal(0)
    if line.unit.lower().startswith("mon"):
        price = line.price or (line.total * 6 / q if q else line.total)
        return 1, price.quantize(CENT), Decimal(1)
    count = int(q) if q > 0 and q == q.to_integral_value() else 1
    price = line.price or line.total
    full = price * count
    share = line.total / full if full and 0 < line.total < full else Decimal(1)
    return count, price.quantize(CENT), share


def unshare(total, share):
    """The full price behind a share of it, snapped to five cents when that is near."""
    price = total / share
    snapped = (price * 20).quantize(Decimal(1)) / 20
    return (snapped if abs(snapped - price) <= Decimal("0.03") else price).quantize(CENT)


@dataclass
class PlanItem:
    article: str
    text: str
    vat: Decimal
    quarter: str
    count: int
    price: Decimal


def vat_of(invoice, line):
    rate = invoice.rates.get(line.vat_code)
    return VAT_NOW.get(rate, rate) if rate is not None else None


def plan(invoices):
    """(items, newest invoice, share) of a grave, read off its last care invoice of each half.

    The share is below 1 when FriedWin bills the grave to several payers; the
    prices returned are the grave's full ones.
    """
    latest = {}
    for inv in invoices:
        half = inv.half[1]
        if half not in latest or (inv.date, inv.number) > (latest[half].date, latest[half].number):
            latest[half] = inv
    if not latest:
        return [], None, Decimal(1)
    newest = max(latest.values(), key=lambda i: (i.date, i.number))
    items, share = {}, Decimal(1)
    # The newer half last, so its care line is the one that stays.
    for inv in sorted(latest.values(), key=lambda i: (i.date, i.number)):
        inv_share = Decimal(1)
        care = next((line for line in inv.lines if is_care_line(line)), None)
        if care:
            count, price, inv_share = care_terms(care)
            items[(CARE_ITEM, None)] = PlanItem(
                CARE_ITEM, CARE_DESCRIPTION, vat_of(inv, care), None, count, price
            )
            share = inv_share
        for line in inv.lines:
            if is_care_line(line) or line.article in ("", CARE_ITEM):
                continue
            if not line.quarter or line.total <= 0:
                continue
            count, price = 1, line.total
            q = line.quantity
            if q and q > 0 and q == q.to_integral_value() and line.price > 0 and q * line.price == line.total:
                count, price = int(q), line.price
            elif inv_share < 1:
                price = unshare(line.total, inv_share)
            key = (line.article, line.quarter)
            if key in items:
                prior = items[key]
                if prior.price == price:
                    prior.count += count
                else:
                    prior.price = prior.price * prior.count + price * count
                    prior.count = 1
            else:
                items[key] = PlanItem(line.article, line.text, vat_of(inv, line), line.quarter, count, price)
    return list(items.values()), newest, share


# --------------------------------------------------------------------------- output


def split_site(site):
    """(name, FriedWin's cemetery text or None) of a "GRABSTÄTTE:" text."""
    site = " ".join(site.split())
    m = CEMETERY_SUFFIX.search(site)
    return (site[: m.start()], m.group(1)) if m else (site, None)


def printed_cemetery(invoices):
    """The app's cemetery as the newest invoice naming one prints it, or None."""
    for inv in sorted(invoices, key=lambda i: (i.date, i.number), reverse=True):
        _, printed = split_site(inv.site)
        if printed:
            return CEMETERIES[printed]
    return None


def csv_text(header, rows):
    buffer = io.StringIO()
    writer = csv.writer(buffer, lineterminator="\r\n")
    writer.writerow(header)
    writer.writerows(rows)
    return buffer.getvalue()


def most_common(values):
    return collections.Counter(values).most_common(1)[0][0]


def graves_count(n):
    return "1 Grab" if n == 1 else f"{n} Gräber"


def write_pdf_report(path, root, archive, csv_name, found, grave_rows, plans, left_out, notes, cooperative,
                     incomplete, contents, before_import):
    """The PDF report: how many graves were found, prepared and left out, and why.

    `found` is (active, ended) over every grave record; `cooperative` maps the
    numbers of the graves imported as cooperative graves to (contract number,
    customer record, care price), `incomplete` the numbers of ordinary graves
    imported without a recipient and/or care item to what is missing; `contents` the rest of the
    archive as (label, count) rows, and `before_import` what the app must hold
    first. Like the text report, it names graves by number only.
    """
    converted = {row[0]: plans[row[0]][2] for row in grave_rows}
    if len(converted) + len(left_out) != sum(found):
        raise SystemExit(f"report: {len(converted)} prepared and {len(left_out)} left out "
                         f"do not add up to the {sum(found)} graves found")

    def counts(actives):
        actives = list(actives)
        return [str(len(actives)), str(sum(actives)), str(len(actives) - sum(actives))]

    doc = Document("FriedWin-Umwandlung: Bericht")
    doc.heading("Bericht zur Umwandlung des FriedWin-Exports")
    doc.paragraph(
        f"Export {os.path.basename(os.path.normpath(root))} · erstellt am "
        f"{datetime.datetime.now():%d.%m.%Y um %H:%M} Uhr · Importarchiv {os.path.basename(archive)}",
        size=9, gray=0.4, after=4,
    )

    doc.heading("Übersicht", 2)
    doc.paragraph(
        "Jeder Grabsatz in FriedWin (GRABSTA1.DAT) ist entweder zum Import vorbereitet oder "
        "weggelassen. „Aktiv“ heißt: Der Grabsatz nennt kein Pflegeende, oder es liegt nicht vor dem "
        "laufenden Monat."
    )
    doc.table(
        ["Gräber", "Anzahl", "aktiv", "beendet"],
        [
            ["Zum Import vorbereitet", *counts(converted.values())],
            ["Weggelassen", *counts(entry[2] for entry in left_out)],
            ["In FriedWin gefunden", str(sum(found)), str(found[0]), str(found[1])],
        ],
        widths=[0.52, 0.16, 0.16, 0.16], align="lrrr", emphasise_last=True,
    )

    by_cemetery = collections.defaultdict(list)
    for row in grave_rows:
        by_cemetery[row[3]].append(converted[row[0]])
    doc.heading("Vorbereitet nach Friedhof", 3)
    doc.table(
        ["Friedhof", "Anzahl", "aktiv", "beendet"],
        [[name, *counts(by_cemetery[name])] for name in sorted(by_cemetery)]
        + [["Zusammen", *counts(converted.values())]],
        widths=[0.52, 0.16, 0.16, 0.16], align="lrrr", emphasise_last=True,
    )

    by_reason = collections.defaultdict(list)
    for entry in left_out:
        by_reason[entry[0]].append(entry)
    reasons = sorted(by_reason, key=lambda r: (-len(by_reason[r]), list(LEFT_OUT).index(r)))
    doc.heading("Weggelassen nach Grund", 3)
    if reasons:
        doc.table(
            ["Grund", "Anzahl", "aktiv", "beendet"],
            [[LEFT_OUT[r][1], *counts(e[2] for e in by_reason[r])] for r in reasons]
            + [["Zusammen", *counts(e[2] for e in left_out)]],
            widths=[0.52, 0.16, 0.16, 0.16], align="lrrr", emphasise_last=True,
        )
    else:
        doc.paragraph("Kein Grab ist weggelassen.")

    doc.heading("Außerdem im Importarchiv", 3)
    doc.table(["Inhalt", "Anzahl"], contents, widths=[0.84, 0.16], align="lr")
    doc.paragraph(before_import)

    if cooperative:
        doc.page_break()
        doc.heading("Als Genossenschaftsgrab übernommen", 2)
        doc.paragraph(NOTES["cooperative"][2])
        doc.paragraph(
            f"{graves_count(len(cooperative))}. „Kundensatz“ sagt, ob FriedWin zum Grab einen "
            "brauchbaren Kundensatz hat (Name und Anschrift); ohne ihn hat ein Grab, das FriedWin nie "
            "abgerechnet hat, keinen Empfänger."
        )
        doc.table(
            ["Grabnummer", "Vertragsnummer", "Kundensatz", "Pflegepreis"],
            [[n, contract, "ja" if usable else "nein", euro(price) if price else "–"]
             for n, (contract, usable, price) in sorted(cooperative.items())],
            widths=[0.18, 0.42, 0.18, 0.22], align="llll",
        )

    if incomplete:
        doc.heading(f"{NOTES['incomplete'][1]}: {graves_count(len(incomplete))}", 2)
        doc.paragraph(NOTES["incomplete"][2])
        doc.table(
            ["Grabnummer", "Es fehlt"],
            [[n, missing] for n, missing in sorted(incomplete.items())],
            widths=[0.3, 0.7], align="ll",
        )

    doc.page_break()
    doc.heading("Weggelassene Gräber im Einzelnen", 2)
    doc.paragraph(
        "Je Grund die Erklärung und jedes Grab, das er betrifft: aktive Gräber zuerst, dann nach "
        "Grabnummer. Ein Grab steht unter dem ersten Grund, an dem es scheitert."
    )
    for r in reasons:
        entries = sorted(by_reason[r], key=lambda e: (not e[2], e[1]))
        doc.heading(f"{LEFT_OUT[r][1]}: {graves_count(len(entries))}", 3)
        doc.paragraph(LEFT_OUT[r][2].format(csv=csv_name))
        doc.table(
            ["Grabnummer", "Status", "Pflegebeginn", "Pflegeende", "Einzelheiten"],
            [[e[1], "aktiv" if e[2] else "beendet", shown_month(e[3]), shown_month(e[4]), e[5]]
             for e in entries],
            widths=[0.15, 0.12, 0.15, 0.15, 0.43],
        )

    noted = {key: sorted(n for n in numbers if n in converted) for key, numbers in notes.items()}
    noted = {key: numbers for key, numbers in noted.items() if numbers and key not in ("cooperative", "incomplete")}
    if noted:
        doc.heading("Vorbereitete Gräber mit Hinweisen", 2)
        doc.paragraph(
            "Diese Gräber sind im Importarchiv, aber ein Wert ist angenommen oder ergänzt. Es lohnt "
            "sich, sie nach dem Import in der App anzusehen. Ein Grab kann unter mehreren Hinweisen "
            "stehen."
        )
        for key in sorted(noted, key=lambda k: (-len(noted[k]), list(NOTES).index(k))):
            doc.heading(f"{NOTES[key][1]}: {graves_count(len(noted[key]))}", 3)
            doc.paragraph(NOTES[key][2])
            doc.numbers(noted[key])

    doc.save(path)


def main():
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("root", help="the export folder, e.g. export/ZWI/20260930-1807")
    parser.add_argument("--out", required=True, help="the import archive to write (.zip)")
    parser.add_argument(
        "--positions",
        help="CSV of GraveNumber,Position that overrides the position read off the location text",
    )
    parser.add_argument(
        "--salutation",
        choices=("omit", "name"),
        default="name",
        help="'name' (the default) puts FriedWin's Anrede (Herrn, Frau, Familie ...) in front of the name",
    )
    args = parser.parse_args()

    stem = os.path.splitext(args.out)[0]
    today = datetime.date.today().strftime("%Y-%m")
    # the year the export was taken, from its folder name (20260930-1807)
    stamp = os.path.basename(os.path.normpath(args.root))
    export_year = stamp[:4] if re.match(r"\d{4}", stamp) else today[:4]
    overrides = {}
    if args.positions:
        with open(args.positions, newline="", encoding="utf-8") as fh:
            for row in csv.DictReader(fh):
                overrides[row["GraveNumber"].strip()] = row["Position"].strip()

    report = collections.Counter()
    attention = collections.defaultdict(list)
    never_invoiced = []
    cooperative = {}  # grave number -> (contract number, customer record usable, care price)
    incomplete = {}  # grave number -> what the grave was imported without
    care_only = {}  # grave number -> care price, for graves that get the care item and nothing else
    left_out = []  # (reason, grave number, active, care start, care end, detail)
    notes = collections.defaultdict(list)

    def leave_out(reason, number, active, start, end, detail=""):
        attention[LEFT_OUT[reason][0]].append(number)
        left_out.append((reason, number, active, start, end, detail))

    def note(key, number):
        attention[NOTES[key][0]].append(number)
        notes[key].append(number)

    customers = read_customers(args.root)
    cemeteries = read_cemeteries(args.root)
    graves = read_graves(args.root)
    invoices = read_invoices(args.root, {g.customer for g in graves})

    # ---- recipients
    recipient_rows = []
    for c in customers.values():
        name = c.name
        if args.salutation == "name" and c.salutation:
            name = f"{c.salutation} {name}"
        if not name or not c.address:
            attention["recipient without name or address (left out)"].append(c.number)
            continue
        recipient_rows.append([c.number, name, "\n".join(c.address), c.email])
        if c.email:
            report["recipients with e-mail"] += 1
    known_recipients = {row[0] for row in recipient_rows}
    usable_records = set(known_recipients)

    # ---- graves and their plans
    seen_numbers = collections.Counter()
    grave_rows, plans = [], {}
    for g in graves:
        seen_numbers[g.number] += 1
        number = g.number if seen_numbers[g.number] == 1 else f"{g.number}-{seen_numbers[g.number]}"
        if seen_numbers[g.number] > 1:
            note("duplicate", number)
        items, newest, share = plan(invoices.get(g.customer, []))
        active = g.end is None or g.end >= today
        report["graves active" if active else "graves ended"] += 1

        field_number, kind, side = parse_location(g.location)
        position = overrides.get(number) or position_of(kind, side)
        report[f"position {position}"] += 1
        if kind is None and number not in overrides:
            note("position-unknown", number)
        if not field_number:
            field_number = "-"
            note("no-field", number)

        never_billed = not items or not any(i.quarter is None for i in items)
        # A cooperative grave has a contract number and no care invoice in the year of the export.
        is_cooperative = (
            active
            and bool(g.contract)
            and not any(i.date.year == int(export_year) for i in invoices.get(g.customer, []))
        )
        # An active grave FriedWin never billed for care (or last billed in DM): what it gets
        # is not readable off any invoice.
        from_record = active and (never_billed or newest.date.year < 2002)
        if never_billed:
            customer = customers.get(g.customer)
            never_invoiced.append(
                [number, "aktiv" if active else "beendet", g.start or "", g.end or "",
                 money(g.care_price) if g.care_price else "", field_number, position, g.site,
                 customer.name if customer else "", ""]
            )
            if not from_record:
                price = (f"Pflegepreis laut Grabsatz {euro(g.care_price)}" if g.care_price
                         else "kein Pflegepreis im Grabsatz")
                leave_out("never-billed", number, active, g.start, g.end, price)
                continue
        if from_record:
            site = g.site
            if not site:
                site = "-"
                note("empty-record", number)
            cemetery = cemeteries.get(g.cemetery)
            if not cemetery:
                leave_out("no-cemetery", number, active, g.start, g.end, f"Friedhofscode {g.cemetery}")
                continue
            start = g.start
            if not start:
                start = f"{export_year}-01"
                note("no-start", number)
            if g.end and g.end < start:
                leave_out("ends-before-start", number, active, start, g.end,
                          "" if g.start else "Pflegebeginn: Januar des Exportjahres")
                continue
            usable = g.customer in usable_records
            if is_cooperative:
                note("cooperative", number)
                cooperative[number] = (g.contract, usable, g.care_price)
                report["graves cooperative"] += 1
            else:
                missing = [what for what, ok in (("Empfänger", usable), ("Leistung", g.care_price)) if not ok]
                if missing:
                    note("incomplete", number)
                    incomplete[number] = " und ".join(missing)
            if never_billed:
                never_invoiced[-1][-1] = "Genossenschaftsgrab" if is_cooperative else "normales Grab"
            if g.care_price:
                care_only[number] = g.care_price
            report["graves converted"] += 1
            report[f"cemetery {cemetery}"] += 1
            grave_rows.append([number, field_number, site, cemetery, position, start, g.end or "",
                               "true" if is_cooperative else "false", g.contract if is_cooperative else ""])
            plans[number] = (g.customer if usable else None, [], active)
            continue
        # the printed name, which the grave record may hold cut short, without the cemetery
        site = split_site(newest.site)[0] or g.site
        if not site:
            leave_out("no-site", number, active, g.start, g.end)
            continue
        cemetery = cemeteries.get(g.cemetery)
        if not cemetery:
            leave_out("no-cemetery", number, active, g.start, g.end, f"Friedhofscode {g.cemetery}")
            continue
        if printed_cemetery(invoices[g.customer]) not in (None, cemetery):
            note("cemetery-differs", number)
        start = g.start
        if not start:
            start = min(i.date for i in invoices[g.customer]).strftime("%Y-%m")
            note("no-start", number)
        if g.end and g.end < start:
            leave_out("ends-before-start", number, active, start, g.end,
                      "" if g.start else "Pflegebeginn aus der ersten Rechnung")
            continue
        if any(i.vat is None for i in items):
            leave_out("no-vat", number, active, start, g.end)
            continue
        if newest.date.year < 2002:
            leave_out("before-euro", number, active, start, g.end,
                      f"letzte Pflege-Rechnung vom {newest.date:%d.%m.%Y}")
            continue
        if g.customer not in known_recipients:
            # 84-91 of the invoice header: 86 salutation, 87 name, 88-91 the rest
            a = newest.address
            name, lines = " ".join(x for x in a[2:4] if x), [x for x in a[4:8] if x]
            if a[3] and lines:
                recipient_rows.append([g.customer, name, "\n".join(lines), ""])
                known_recipients.add(g.customer)
                note("invoice-address", number)
            else:
                leave_out("no-customer", number, active, start, g.end)
                continue

        # The grave record holds the care price FriedWin bills next; the last invoice
        # may predate a raise.
        care = next(i for i in items if i.quarter is None)
        if g.care_price:
            if abs(g.care_price - care.price) > care.price / 10:
                note("price-differs", number)
            care.price = g.care_price
        if share < 1:
            note("shares", number)
        report["graves converted"] += 1
        report[f"cemetery {cemetery}"] += 1
        if is_cooperative:
            note("cooperative", number)
            cooperative[number] = (g.contract, g.customer in usable_records, g.care_price)
            report["graves cooperative"] += 1
        grave_rows.append([number, field_number, site, cemetery, position, start, g.end or "",
                           "true" if is_cooperative else "false", g.contract if is_cooperative else ""])
        plans[number] = (g.customer, items, active)

    # ---- items: one per FriedWin article and quarter
    quarters_of = collections.defaultdict(set)
    for _, items, _ in plans.values():
        for i in items:
            quarters_of[i.article].add(i.quarter)

    def item_number(i):
        if len(quarters_of[i.article]) == 1:
            return i.article
        return f"{i.article}-{QUARTER_SUFFIX.get(i.quarter, 'J')}"

    texts, vats, prices, quarter_of = (collections.defaultdict(list) for _ in range(4))
    for _, items, active in plans.values():
        for i in items:
            n = item_number(i)
            texts[n].append(i.text)
            vats[n].append(i.vat)
            quarter_of[n] = i.quarter
            prices[n].append((active, i.price))
    default_price = {}
    item_rows = []
    for n in sorted(texts):
        # The price most active graves pay; ended graves only when no active one has it.
        current = [p for active, p in prices[n] if active] or [p for _, p in prices[n]]
        default_price[n] = most_common(current)
        if len(set(vats[n])) > 1:
            attention["item billed at several VAT rates, most common taken"].append(n)
        item_rows.append(
            [n, most_common(texts[n]), f"{most_common(vats[n]).normalize():f}", money(default_price[n]),
             quarter_of[n] or ""]
        )

    mapping_rows = []
    for number, (customer, items, _) in plans.items():
        if customer:
            mapping_rows.append(["Recipient", number, customer, "", "", ""])
        if number in care_only:
            price = care_only[number]
            if CARE_ITEM not in default_price:
                raise SystemExit(f"item {CARE_ITEM} is in no billed grave's items, a grave without invoices needs it")
            own = "" if default_price[CARE_ITEM] == price else money(price)
            mapping_rows.append(["Item", number, CARE_ITEM, "1", "false", own])
        for i in items:
            n = item_number(i)
            own = "" if default_price[n] == i.price else money(i.price)
            mapping_rows.append(["Item", number, n, str(i.count), "false", own])

    files = {
        "recipients.csv": csv_text(["RecipientNumber", "Name", "Address", "Email"], recipient_rows),
        "items.csv": csv_text(["ItemNumber", "Description", "VatRate", "Price", "Quarter"], item_rows),
        "graves.csv": csv_text(
            ["GraveNumber", "FieldNumber", "SiteName", "Cemetery", "Position", "StartDate", "EndDate", "Cooperative",
             "ContractNumber"],
            grave_rows,
        ),
        "mapping.csv": csv_text(
            ["Type", "GraveNumber", "Reference", "Count", "PriceIncludesVat", "UnitPrice"], mapping_rows
        ),
    }
    with zipfile.ZipFile(args.out, "w", zipfile.ZIP_DEFLATED) as z:
        for name, content in files.items():
            z.writestr(name, content.encode("utf-8"))

    # The graves left out for never having been billed, for the user to look
    # through; this file names people, the printed report does not.
    with open(f"{stem}-nie-abgerechnet.csv", "w", newline="", encoding="utf-8-sig") as fh:
        writer = csv.writer(fh, delimiter=";")
        writer.writerow(["Grabnummer", "Status", "Pflegebeginn", "Pflegeende", "Pflegepreis laut Grabsatz",
                         "Feld", "Lage", "Grabstätte", "Kunde", "Übernommen als"])
        writer.writerows(sorted(never_invoiced, key=lambda r: (r[1], r[0])))

    vat_rates = sorted({r[2] for r in item_rows}, key=Decimal)
    cemeteries = sorted({r[3] for r in grave_rows})
    write_pdf_report(
        f"{stem}-report.pdf", args.root, args.out, os.path.basename(f"{stem}-nie-abgerechnet.csv"),
        (report["graves active"], report["graves ended"]), grave_rows, plans, left_out, notes,
        cooperative, incomplete,
        [
            ["Empfänger", str(len(recipient_rows))],
            ["davon mit E-Mail-Adresse", str(report["recipients with e-mail"])],
            ["davon aus der Anschrift der letzten Rechnung gebildet", str(len(notes["invoice-address"]))],
            ["Kunden ohne Name oder Anschrift, weggelassen (Kundennummern im Textbericht)",
             str(len(attention["recipient without name or address (left out)"]))],
            ["Leistungen", str(len(item_rows))],
            ["Zuordnungen von Empfängern und Leistungen zu Gräbern", str(len(mapping_rows))],
        ],
        "Vor dem Import müssen in den Einstellungen der App die Steuersätze "
        + " und ".join(f"{rate} %" for rate in vat_rates)
        + " und die Friedhöfe " + ", ".join(cemeteries) + " angelegt sein.",
    )

    lines = [
        f"wrote {args.out}, {stem}-report.pdf and {stem}-nie-abgerechnet.csv",
        f"  recipients {len(recipient_rows)}, items {len(item_rows)}, graves {len(grave_rows)}, "
        f"links {len(mapping_rows)}",
        f"  VAT rates used: {sorted({r[2] for r in item_rows})} (must exist in the app before importing)",
        f"  cemeteries used: {sorted({r[3] for r in grave_rows})} (must exist in the settings before importing)",
    ]
    lines += [f"  {key}: {value}" for key, value in sorted(report.items())]
    if cooperative:
        lines.append("cooperative graves (number, contract, customer record, care price):")
        lines += [f"  {n}  {contract}  {'ja' if usable else 'nein'}  {money(price) if price else '-'}"
                  for n, (contract, usable, price) in sorted(cooperative.items())]
    if incomplete:
        lines.append("graves imported without recipient or care item (number, missing):")
        lines += [f"  {n}  {missing}" for n, missing in sorted(incomplete.items())]
    if attention:
        lines.append("needs attention (grave or item numbers):")
        for key, numbers in sorted(attention.items(), key=lambda kv: -len(kv[1])):
            lines.append(f"  {len(numbers):4d}  {key}: {', '.join(numbers)}")
    with open(f"{stem}-report.txt", "w", encoding="utf-8") as fh:
        fh.write("\n".join(lines) + "\n")
    for line in lines:
        print(line if len(line) < 200 else line[:200] + " ...")


if __name__ == "__main__":
    main()
