from pathlib import Path

from reportlab.lib import colors
from reportlab.lib.enums import TA_LEFT, TA_RIGHT
from reportlab.lib.pagesizes import A4
from reportlab.lib.styles import ParagraphStyle
from reportlab.lib.units import mm
from reportlab.pdfbase.pdfmetrics import stringWidth
from reportlab.pdfgen import canvas
from reportlab.platypus import Paragraph, Table, TableStyle


OUTPUT = Path(r"C:\Users\Johan\source\repos\Virtual Company\output\pdf\example-supplier-invoice-valid-iban.pdf")

INVOICE_NUMBER = "VC-EX-2026-002"
VALID_SWEDISH_IBAN = "SE35 5000 0000 0549 1000 0003"


def money(value: float) -> str:
    return f"{value:,.2f}".replace(",", " ")


def draw_label_value(pdf, label, value, x, y, width, label_width=34 * mm):
    pdf.setFillColor(colors.HexColor("#5E6B78"))
    pdf.setFont("Helvetica", 8.5)
    pdf.drawString(x, y, label.upper())
    pdf.setFillColor(colors.HexColor("#15202B"))
    pdf.setFont("Helvetica-Bold", 9)
    pdf.drawRightString(x + width, y, value)


def build_invoice():
    OUTPUT.parent.mkdir(parents=True, exist_ok=True)
    pdf = canvas.Canvas(str(OUTPUT), pagesize=A4)
    width, height = A4

    navy = colors.HexColor("#16324F")
    teal = colors.HexColor("#19A7A0")
    pale = colors.HexColor("#F2F7F8")
    ink = colors.HexColor("#15202B")
    muted = colors.HexColor("#5E6B78")
    line = colors.HexColor("#D7E1E5")

    pdf.setTitle(f"Example Supplier Invoice {INVOICE_NUMBER}")
    pdf.setAuthor("Example Company AB")
    pdf.setSubject("Sample invoice - not for payment")

    # Top band and identity.
    pdf.setFillColor(navy)
    pdf.rect(0, height - 39 * mm, width, 39 * mm, fill=1, stroke=0)
    pdf.setFillColor(colors.white)
    pdf.setFont("Helvetica-Bold", 17)
    pdf.drawString(18 * mm, height - 19 * mm, "EXAMPLE COMPANY AB")
    pdf.setFont("Helvetica", 8.5)
    pdf.drawString(18 * mm, height - 27 * mm, "Sveavagen 100  |  111 34 Stockholm  |  Sweden")
    pdf.drawString(18 * mm, height - 33 * mm, "example@example.com  |  +46 8 000 00 00")
    pdf.setFont("Helvetica-Bold", 25)
    pdf.drawRightString(width - 18 * mm, height - 23 * mm, "INVOICE")

    # Prominent sample watermark banner.
    banner_y = height - 53 * mm
    pdf.setFillColor(colors.HexColor("#FFF4D8"))
    pdf.roundRect(18 * mm, banner_y, width - 36 * mm, 10 * mm, 2 * mm, fill=1, stroke=0)
    pdf.setFillColor(colors.HexColor("#8A5A00"))
    pdf.setFont("Helvetica-Bold", 10)
    pdf.drawCentredString(width / 2, banner_y + 3.6 * mm, "SAMPLE - NOT FOR PAYMENT")

    # Parties.
    top = height - 73 * mm
    pdf.setFillColor(muted)
    pdf.setFont("Helvetica-Bold", 8)
    pdf.drawString(18 * mm, top, "BILL TO")
    pdf.setFillColor(ink)
    pdf.setFont("Helvetica-Bold", 11)
    pdf.drawString(18 * mm, top - 7 * mm, "Demo Customer AB")
    pdf.setFont("Helvetica", 9)
    pdf.drawString(18 * mm, top - 13 * mm, "Customer Street 1")
    pdf.drawString(18 * mm, top - 19 * mm, "123 45 Stockholm, Sweden")
    pdf.drawString(18 * mm, top - 25 * mm, "Org. no. 556000-0000")

    meta_x = 112 * mm
    meta_w = width - meta_x - 18 * mm
    draw_label_value(pdf, "Invoice number", INVOICE_NUMBER, meta_x, top, meta_w)
    draw_label_value(pdf, "Invoice date", "2026-09-03", meta_x, top - 8 * mm, meta_w)
    draw_label_value(pdf, "Due date", "2026-10-03", meta_x, top - 16 * mm, meta_w)
    draw_label_value(pdf, "Payment terms", "30 days", meta_x, top - 24 * mm, meta_w)
    draw_label_value(pdf, "Currency", "SEK", meta_x, top - 32 * mm, meta_w)

    # Line items.
    table_y = height - 128 * mm
    data = [
        ["DESCRIPTION", "QTY", "UNIT PRICE", "VAT", "AMOUNT"],
        ["Business consulting services - September 2026", "10 h", money(1200), "25%", money(12000)],
    ]
    col_widths = [87 * mm, 19 * mm, 29 * mm, 18 * mm, 31 * mm]
    table = Table(data, colWidths=col_widths, rowHeights=[10 * mm, 17 * mm])
    table.setStyle(TableStyle([
        ("BACKGROUND", (0, 0), (-1, 0), navy),
        ("TEXTCOLOR", (0, 0), (-1, 0), colors.white),
        ("FONTNAME", (0, 0), (-1, 0), "Helvetica-Bold"),
        ("FONTSIZE", (0, 0), (-1, 0), 8),
        ("ALIGN", (1, 0), (-1, -1), "RIGHT"),
        ("ALIGN", (0, 0), (0, -1), "LEFT"),
        ("VALIGN", (0, 0), (-1, -1), "MIDDLE"),
        ("FONTNAME", (0, 1), (-1, -1), "Helvetica"),
        ("FONTSIZE", (0, 1), (-1, -1), 8.5),
        ("TEXTCOLOR", (0, 1), (-1, -1), ink),
        ("BACKGROUND", (0, 1), (-1, -1), pale),
        ("LINEBELOW", (0, 1), (-1, -1), 0.6, line),
        ("LEFTPADDING", (0, 0), (-1, -1), 4 * mm),
        ("RIGHTPADDING", (0, 0), (-1, -1), 4 * mm),
    ]))
    table.wrapOn(pdf, width, height)
    table.drawOn(pdf, 18 * mm, table_y - 27 * mm)

    # Totals block.
    totals_x = 113 * mm
    totals_y = table_y - 42 * mm
    totals_w = width - totals_x - 18 * mm
    rows = [
        ("Subtotal", money(12000)),
        ("VAT amount", money(3000)),
    ]
    for label, value in rows:
        pdf.setFont("Helvetica", 9)
        pdf.setFillColor(muted)
        pdf.drawString(totals_x, totals_y, label)
        pdf.setFillColor(ink)
        pdf.drawRightString(totals_x + totals_w, totals_y, value)
        totals_y -= 8 * mm
    pdf.setStrokeColor(teal)
    pdf.setLineWidth(1.5)
    pdf.line(totals_x, totals_y + 3.5 * mm, totals_x + totals_w, totals_y + 3.5 * mm)
    pdf.setFillColor(navy)
    pdf.setFont("Helvetica-Bold", 11)
    pdf.drawString(totals_x, totals_y - 3 * mm, "TOTAL SEK")
    pdf.setFont("Helvetica-Bold", 15)
    pdf.drawRightString(totals_x + totals_w, totals_y - 3 * mm, money(15000))

    # Payment details and notes.
    box_y = 50 * mm
    box_w = 83 * mm
    box_h = 37 * mm
    pdf.setFillColor(pale)
    pdf.roundRect(18 * mm, box_y, box_w, box_h, 2 * mm, fill=1, stroke=0)
    pdf.setFillColor(navy)
    pdf.setFont("Helvetica-Bold", 9)
    pdf.drawString(23 * mm, box_y + 28 * mm, "PAYMENT DETAILS (EXAMPLE ONLY)")
    pdf.setFillColor(ink)
    pdf.setFont("Helvetica", 8.5)
    pdf.drawString(23 * mm, box_y + 20 * mm, "Bankgiro: 000-0000")
    pdf.drawString(23 * mm, box_y + 14 * mm, f"Reference: {INVOICE_NUMBER}")
    pdf.drawString(23 * mm, box_y + 8 * mm, f"IBAN: {VALID_SWEDISH_IBAN}")

    note_x = 112 * mm
    pdf.setFillColor(navy)
    pdf.setFont("Helvetica-Bold", 9)
    pdf.drawString(note_x, box_y + 28 * mm, "NOTES")
    note = Paragraph(
        "This invoice is a fictional example created for demonstration. "
        "All company, customer, bank, and registration details are placeholders. "
        "Do not use it for accounting or payment.",
        ParagraphStyle(
            "note",
            fontName="Helvetica",
            fontSize=8.5,
            leading=12,
            textColor=ink,
            alignment=TA_LEFT,
        ),
    )
    note.wrapOn(pdf, width - note_x - 18 * mm, 25 * mm)
    note.drawOn(pdf, note_x, box_y + 8 * mm)

    # Footer.
    pdf.setStrokeColor(line)
    pdf.setLineWidth(0.5)
    pdf.line(18 * mm, 32 * mm, width - 18 * mm, 32 * mm)
    pdf.setFillColor(muted)
    pdf.setFont("Helvetica", 7.5)
    pdf.drawString(18 * mm, 24 * mm, "Example Company AB  |  Org. no. 559999-9999  |  VAT no. SE559999999901")
    pdf.drawString(18 * mm, 18 * mm, "Registered office: Stockholm  |  Approved for F-tax (example statement)")
    pdf.drawRightString(width - 18 * mm, 18 * mm, "Page 1 of 1")

    pdf.save()


if __name__ == "__main__":
    build_invoice()
