"""Gives the three Gilroy files one family name, "Gilroy", with their real weights (500 Medium, 600 SemiBold,
600 SemiBold Italic), and writes them to Corner.Mac/Assets/fonts.

The files as supplied each name a family of their own ("Gilroy-Medium", ...) and all say weight 400. Windows'
WPF happened to group them anyway; Avalonia (and macOS) take the names at their word, so SemiBold text came out
Medium. Only the name table and three weight/style fields change; the glyphs are untouched. Reads ../windows/*.ttf.
"""
import os, struct

HERE = os.path.dirname(os.path.abspath(__file__))
SRC = os.path.normpath(os.path.join(HERE, '..', '..', 'windows'))
OUT = os.path.normpath(os.path.join(HERE, '..', 'Corner.Mac', 'Assets', 'fonts'))
FACES = {
    'Gilroy-Medium.ttf': ('Medium', 500, False),
    'Gilroy-SemiBold.ttf': ('SemiBold', 600, False),
    'Gilroy-SemiBoldItalic.ttf': ('SemiBold Italic', 600, True),
}


def name_table(sub):
    full = 'Gilroy ' + sub
    ps = 'Gilroy-' + sub.replace(' ', '')
    recs = [(1, 'Gilroy'), (2, sub if sub in ('Italic',) else ('Italic' if sub.endswith('Italic') else 'Regular')),
            (4, full), (6, ps), (16, 'Gilroy'), (17, sub)]
    data = b''; rows = []
    for nid, text in recs:
        raw = text.encode('utf-16-be')
        rows.append(struct.pack('>HHHHHH', 3, 1, 0x409, nid, len(raw), len(data)))
        data += raw
    head = struct.pack('>HHH', 0, len(rows), 6 + 12 * len(rows))
    return head + b''.join(rows) + data


def checksum(b):
    b = b + b'\0' * (-len(b) % 4)
    return sum(struct.unpack('>%dI' % (len(b) // 4), b)) & 0xFFFFFFFF


def patch(src, sub, weight, italic):
    b = bytearray(open(src, 'rb').read())
    n = struct.unpack('>H', b[4:6])[0]
    tables = {}
    for i in range(n):
        tag, cs, off, ln = struct.unpack('>4sIII', b[12 + 16 * i:28 + 16 * i])
        tables[tag] = (i, off, ln)
    # OS/2: usWeightClass, fsSelection (italic bit 0, bold bit 5, regular bit 6)
    _, off, _ = tables[b'OS/2']
    struct.pack_into('>H', b, off + 4, weight)
    fs = struct.unpack('>H', b[off + 62:off + 64])[0] & ~(1 | 32 | 64)
    fs |= 1 if italic else 64
    struct.pack_into('>H', b, off + 62, fs)
    # head: macStyle (bold bit 0, italic bit 1)
    _, hoff, _ = tables[b'head']
    struct.pack_into('>H', b, hoff + 44, 2 if italic else 0)
    # a new name table at the end of the file
    nt = name_table(sub)
    i, _, _ = tables[b'name']
    while len(b) % 4: b.append(0)
    noff = len(b); b += nt
    while len(b) % 4: b.append(0)
    struct.pack_into('>4sIII', b, 12 + 16 * i, b'name', checksum(nt), noff, len(nt))
    # checksums: every table we touched, then the font's own adjustment in head
    for tag in (b'OS/2', b'head'):
        j, o, ln = tables[tag]
        if tag == b'head': struct.pack_into('>I', b, o + 8, 0)
        struct.pack_into('>I', b, 12 + 16 * j + 4, checksum(bytes(b[o:o + ln])))
    struct.pack_into('>I', b, hoff + 8, (0xB1B0AFBA - checksum(bytes(b))) & 0xFFFFFFFF)
    return bytes(b)


def main():
    os.makedirs(OUT, exist_ok=True)
    for f, (sub, w, it) in FACES.items():
        src = os.path.join(SRC, f)
        if not os.path.exists(src): print('missing', src); continue
        with open(os.path.join(OUT, f), 'wb') as fh: fh.write(patch(src, sub, w, it))
        print(f, '->', 'Gilroy', sub, w)


if __name__ == '__main__':
    main()
