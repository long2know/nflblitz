"""Read actual PE/CLI/portable-PDB metadata without executing managed code."""
import hashlib
import json
import uuid

MAX_IMAGE_BYTES = 16 * 1024 * 1024


def require(value, code):
    if not value:
        raise ValueError("DEFAULT PE SEAL: " + code)


class Image:
    def __init__(self, data):
        require(isinstance(data, bytes) and 0 < len(data) <= MAX_IMAGE_BYTES, "image-size")
        self.data = data

    def take(self, offset, size):
        require(0 <= offset and 0 <= size and offset + size <= len(self.data), "bounds")
        return self.data[offset:offset + size]

    def number(self, offset, size):
        return int.from_bytes(self.take(offset, size), "little")


CODED = {
    "TypeDefOrRef": (2, (2, 1, 27)), "HasConstant": (2, (4, 8, 23)),
    "HasCustomAttribute": (5, (6, 4, 1, 2, 8, 9, 10, 0, 14, 23, 20, 17, 26, 27, 32, 35, 38, 39, 40, 42, 44, 43)),
    "HasFieldMarshal": (1, (4, 8)), "HasDeclSecurity": (2, (2, 6, 32)),
    "MemberRefParent": (3, (2, 1, 26, 6, 27)), "HasSemantics": (1, (20, 23)),
    "MethodDefOrRef": (1, (6, 10)), "MemberForwarded": (1, (4, 6)),
    "Implementation": (2, (38, 35, 39)), "CustomAttributeType": (3, (6, 10)),
    "ResolutionScope": (2, (0, 26, 35, 1)), "TypeOrMethodDef": (1, (2, 6)),
    "HasCustomDebugInformation": (5, (6, 4, 1, 2, 8, 9, 10, 0, 14, 23, 20, 17, 26, 27, 32, 35, 38, 39, 40, 42, 44, 43, 48, 50, 51, 52, 53)),
}
TABLES = {
    0: (2, "s", "g", "g", "g"), 1: ("ResolutionScope", "s", "s"),
    2: (4, "s", "s", "TypeDefOrRef", "t4", "t6"), 3: ("t4",), 4: (2, "s", "b"),
    5: ("t6",), 6: (4, 2, 2, "s", "b", "t8"), 7: ("t8",), 8: (2, 2, "s"),
    9: ("t2", "TypeDefOrRef"), 10: ("MemberRefParent", "s", "b"), 11: (2, "HasConstant", "b"),
    12: ("HasCustomAttribute", "CustomAttributeType", "b"), 13: ("HasFieldMarshal", "b"),
    14: (2, "HasDeclSecurity", "b"), 15: (2, 4, "t2"), 16: (4, "t4"), 17: ("b",),
    18: ("t2", "t20"), 19: ("t20",), 20: (2, "s", "TypeDefOrRef"), 21: ("t2", "t23"),
    22: ("t23",), 23: (2, "s", "b"), 24: (2, "t6", "HasSemantics"),
    25: ("t2", "MethodDefOrRef", "MethodDefOrRef"), 26: ("s",), 27: ("b",),
    28: (2, "MemberForwarded", "s", "t26"), 29: (4, "t4"), 30: (4, 4), 31: (4,),
    32: (4, 2, 2, 2, 2, 4, "b", "s", "s"), 33: (4,), 34: (4, 4, 4),
    35: (2, 2, 2, 2, 4, "b", "s", "s", "b"), 36: (4, "t35"), 37: (4, 4, 4, "t35"),
    38: (4, "s", "b"), 39: (4, 4, "s", "s", "Implementation"),
    40: (4, 4, "s", "Implementation"), 41: ("t2", "t2"),
    42: (2, 2, "TypeOrMethodDef", "s"), 43: ("MethodDefOrRef", "b"), 44: ("t42", "TypeDefOrRef"),
    48: ("b", "g", "b", "g"), 49: ("t48", "b"),
    50: ("t6", "t53", "t51", "t52", 4, 4), 51: (2, 2, "s"), 52: ("s", "b"),
    53: ("t53", "b"), 54: ("t6", "t6"), 55: ("HasCustomDebugInformation", "g", "b"),
}


def compressed(data, offset):
    require(offset < len(data), "compressed-index")
    first = data[offset]
    count = 1 if first < 128 else 2 if first < 192 else 4
    require(first < 224 and offset + count <= len(data), "compressed-index")
    mask = 127 if count == 1 else 63 if count == 2 else 31
    value = first & mask
    for byte in data[offset + 1:offset + count]:
        value = (value << 8) | byte
    return value, offset + count


class Metadata:
    def __init__(self, data):
        image = Image(data)
        require(image.take(0, 4) == b"BSJB", "metadata-signature")
        version = image.number(12, 4)
        require(0 < version <= 256, "metadata-version")
        position = (16 + version + 3) & ~3
        count = image.number(position + 2, 2)
        require(0 < count <= 32, "stream-count")
        position += 4
        self.locations, self.streams = {}, {}
        for _ in range(count):
            offset, size = image.number(position, 4), image.number(position + 4, 4)
            start = position + 8
            name = image.take(start, min(32, len(data) - start))
            require(b"\0" in name, "stream-name")
            end = name.index(0)
            name = name[:end].decode("ascii")
            require(name and name not in self.locations, "duplicate-stream")
            self.locations[name] = (offset, size)
            position = (start + end + 4) & ~3
        spans = []
        for name, (offset, size) in self.locations.items():
            require(offset >= position and all(offset + size <= a or offset >= b for a, b in spans),
                    "stream-overlap")
            self.streams[name] = image.take(offset, size)
            if size:
                spans.append((offset, offset + size))
        require("#~" in self.streams and "#Blob" in self.streams, "required-streams")
        self.external = {}
        if "#Pdb" in self.streams:
            pdb = Image(self.streams["#Pdb"])
            mask, at = pdb.number(24, 8), 32
            for table in range(64):
                if mask & (1 << table):
                    self.external[table] = pdb.number(at, 4)
                    at += 4
            require(at == len(pdb.data), "external-rows")
        self.table = Image(self.streams["#~"])
        self.flags, mask = self.table.number(6, 1), self.table.number(8, 8)
        require(self.flags & ~7 == 0, "heap-flags")
        self.rows, self.starts, self.widths = {}, {}, {}
        at = 24
        for table in range(64):
            if mask & (1 << table):
                require(table in TABLES, "unknown-table")
                self.rows[table] = self.table.number(at, 4)
                require(self.rows[table] <= len(self.table.data), "row-count")
                at += 4
        for table, rows in self.rows.items():
            widths = [self.width(column) for column in TABLES[table]]
            self.starts[table], self.widths[table] = at, widths
            self.table.take(at, rows * sum(widths))
            at += rows * sum(widths)
        require(0 <= len(self.table.data) - at <= 4, "table-tail")

    def width(self, column):
        count = lambda table: max(self.rows.get(table, 0), self.external.get(table, 0))
        if isinstance(column, int):
            return column
        if column in ("s", "g", "b"):
            return 4 if self.flags & {"s": 1, "g": 2, "b": 4}[column] else 2
        if column.startswith("t"):
            return 2 if count(int(column[1:])) < 65536 else 4
        bits, tables = CODED[column]
        return 2 if max(count(t) for t in tables) < (1 << (16 - bits)) else 4

    def row(self, table, rid):
        require(table in self.rows and 1 <= rid <= self.rows[table], "row-id")
        at = self.starts[table] + (rid - 1) * sum(self.widths[table])
        row = []
        for width in self.widths[table]:
            row.append(self.table.number(at, width))
            at += width
        return row

    def string(self, index):
        data = self.streams.get("#Strings", b"")
        require(0 <= index < len(data) and b"\0" in data[index:], "string-index")
        return data[index:data.index(0, index)].decode("utf-8")

    def blob(self, index):
        data = self.streams["#Blob"]
        size, at = compressed(data, index)
        require(at + size <= len(data), "blob-index")
        return data[at:at + size]

    def guid(self, index):
        data = self.streams.get("#GUID", b"")
        require(index > 0 and index * 16 <= len(data), "guid-index")
        return data[(index - 1) * 16:index * 16]


def verify(pe_bytes, pdb_bytes, expected_sources):
    pe = Image(pe_bytes)
    require(pe.take(0, 2) == b"MZ", "dos")
    offset = pe.number(0x3c, 4)
    require(pe.take(offset, 4) == b"PE\0\0" and pe.number(offset + 4, 2) in (0x14c, 0x8664), "pe")
    section_count, optional_size = pe.number(offset + 6, 2), pe.number(offset + 20, 2)
    require(0 < section_count <= 32, "section-count")
    optional = offset + 24
    magic = pe.number(optional, 2)
    require(magic in (0x10b, 0x20b), "optional-header")
    directory, count_at = (96, 92) if magic == 0x10b else (112, 108)
    count = pe.number(optional + count_at, 4)
    require(15 <= count <= 32 and optional_size >= directory + count * 8, "directory-count")
    sections = []
    for index in range(section_count):
        at = optional + optional_size + index * 40
        size, start, rva = pe.number(at + 16, 4), pe.number(at + 20, 4), pe.number(at + 12, 4)
        pe.take(start, size)
        require(all(start + size <= a or start >= a + b for a, b, _, _ in sections), "sections-overlap")
        sections.append((start, size, rva, pe.number(at + 36, 4)))
    def file_offset(rva, size):
        found = [start + rva - address for start, length, address, _ in sections
                 if address <= rva and rva + size <= address + length]
        require(len(found) == 1, "rva")
        return found[0]
    def data_directory(index):
        at = optional + directory + index * 8
        return pe.number(at, 4), pe.number(at + 4, 4)
    address, size = data_directory(14)
    cli = file_offset(address, 72)
    require(size >= 72 and 72 <= pe.number(cli, 4) <= size, "cli-header")
    flags, token = pe.number(cli + 16, 4), pe.number(cli + 20, 4)
    require(flags & 1 and not flags & 0x10 and token >> 24 == 6 and token & 0xffffff,
            "managed-method-token")
    require(pe.number(cli + 64, 4) == pe.number(cli + 68, 4) == 0, "native-header")
    size = pe.number(cli + 12, 4)
    md = Metadata(pe.take(file_offset(pe.number(cli + 8, 4), size), size))
    require(md.rows.get(0) == md.rows.get(32) == 1 and not any(md.rows.get(t, 0) for t in (3, 5, 7, 19, 22)),
            "module-or-pointer-tables")
    method_rid = token & 0xffffff
    method = md.row(6, method_rid)
    owners, previous = [], 1
    for rid in range(1, md.rows.get(2, 0) + 1):
        row = md.row(2, rid)
        start = row[5]
        end = md.row(2, rid + 1)[5] if rid < md.rows[2] else md.rows[6] + 1
        require(previous <= start <= end <= md.rows[6] + 1, "method-list")
        previous = start
        if start <= method_rid < end:
            owners.append((rid, row))
    require(len(owners) == 1, "method-owner")
    owner_rid, owner = owners[0]
    require(md.string(owner[1]) == "DefaultCabinetChecks" and md.string(owner[2]) == ""
            and not owner[0] & 0x20, "entry-type")
    require(all(md.row(41, rid)[0] != owner_rid for rid in range(1, md.rows.get(41, 0) + 1)), "nested-entry")
    require(md.string(method[3]) == "Main" and method[2] & 0x10
            and not method[2] & (0x400 | 0x2000) and not method[1] & (3 | 4 | 0x1000)
            and md.blob(method[4]) == b"\0\1\x08\x1d\x0e", "entry-signature")
    body = file_offset(method[0], 1)
    require(any(rva <= method[0] < rva + size and flags & 0x20000000 for _, size, rva, flags in sections),
            "entry-section")
    first = pe.number(body, 1)
    if first & 3 == 2:
        code_size = first >> 2
        file_offset(method[0] + 1, code_size)
    else:
        require(first & 3 == 3, "il-header")
        header = (pe.number(body, 2) >> 12) * 4
        code_size = pe.number(body + 4, 4)
        require(header >= 12, "il-header-size")
        file_offset(method[0], header + code_size)
    require(code_size > 0, "empty-main")
    pdb = Metadata(pdb_bytes)
    identity = pdb.streams.get("#Pdb", b"")
    require(len(identity) >= 32 and int.from_bytes(identity[20:24], "little") == token, "pdb-token")
    require(all(rows == md.rows.get(t, 0) for t, rows in pdb.external.items()), "pdb-type-rows")
    address, size = data_directory(6)
    require(size and size % 28 == 0 and size <= 28 * 32, "debug-directory")
    records = {}
    start = file_offset(address, size)
    for at in range(start, start + size, 28):
        kind = pe.number(at + 12, 4)
        if kind not in (2, 19):
            continue
        require(kind not in records, "duplicate-debug-record")
        data = pe.take(pe.number(at + 24, 4), pe.number(at + 16, 4))
        if kind == 2:
            require(pe.number(at + 8, 2) == 0x100 and pe.number(at + 10, 2) == 0x504d
                    and data[:4] == b"RSDS" and len(data) > 24 and int.from_bytes(data[20:24], "little") == 1,
                    "portable-codeview")
            records[2] = (data[4:20], pe.number(at + 4, 4))
        else:
            require(data.startswith(b"SHA256\0") and len(data) == 39, "pdb-checksum")
            records[19] = data[7:]
    require(set(records) == {2, 19} and records[2] == (identity[:16], int.from_bytes(identity[16:20], "little")),
            "pdb-identity")
    at = pdb.locations["#Pdb"][0]
    require(hashlib.sha256(pdb_bytes[:at] + b"\0" * 20 + pdb_bytes[at + 20:]).digest() == records[19],
            "pdb-content")
    documents = {}
    for rid in range(1, pdb.rows.get(48, 0) + 1):
        name, algorithm, content, language = pdb.row(48, rid)
        require(pdb.guid(algorithm) == uuid.UUID("8829d00f-11b8-4213-878b-770e8597ac16").bytes_le
                and pdb.guid(language) == uuid.UUID("3f5162f8-07c6-11d3-9053-00c04fa302a1").bytes_le, "pdb-language")
        data, parts, pos = pdb.blob(name), [], 1
        require(data and data[0] in (47, 92), "pdb-document-name")
        while pos < len(data):
            index, pos = compressed(data, pos)
            parts.append(pdb.blob(index).decode("utf-8"))
        path = chr(data[0]).join(parts)
        require(path not in documents, "duplicate-pdb-document")
        documents[path] = pdb.blob(content).hex()
    require(documents == expected_sources, "pdb-source-documents")
    return dict(entryPointToken="0x%08x" % token, declaringType="DefaultCabinetChecks", method="Main",
                signature="static int32 Main(string[])", documents=len(documents),
                mvid=str(uuid.UUID(bytes_le=md.guid(md.row(0, 1)[2]))),
                peSha256=hashlib.sha256(pe_bytes).hexdigest(), pdbSha256=hashlib.sha256(pdb_bytes).hexdigest(),
                sourceMapSha256=hashlib.sha256(json.dumps(documents, sort_keys=True, separators=(",", ":")).encode()).hexdigest())
