"""Read-only, hash-bound PE literal references. Results are seeds, not disassembly."""
import hashlib
import json
from pathlib import Path
import re
import struct
import sys

path = Path(sys.argv[1])
data = path.read_bytes()
digest = hashlib.sha256(data).hexdigest()
if digest != '54df25b238356024248085ca7fa6af9005b0be4ec40beef3c32faa8c06b6d4e5':
    raise SystemExit('Unsupported executable fingerprint')
pe = struct.unpack_from('<I', data, 0x3c)[0]
assert data[pe:pe+4] == b'PE\0\0'
base = struct.unpack_from('<I', data, pe+52)[0]
count = struct.unpack_from('<H', data, pe+6)[0]
optional_size = struct.unpack_from('<H', data, pe+20)[0]
sections = []
for i in range(count):
    offset = pe+24+optional_size+i*40
    name = data[offset:offset+8].split(b'\0')[0].decode('ascii')
    virtual_size, rva, size, raw = struct.unpack_from('<IIII', data, offset+8)
    sections.append(dict(name=name, rva=rva, raw=raw, size=size))

def virtual_address(offset):
    for section in sections:
        if section['raw'] <= offset < section['raw'] + section['size']:
            return base + section['rva'] + offset - section['raw']
    return None

references = []
for target in (0x111fa154, 0x111fa034, 0x111fa2c8):
    for section in sections:
        if section['name'] != '.text':
            continue
        code = data[section['raw']:section['raw']+section['size']]
        matches = [dict(address=hex(virtual_address(section['raw']+m.start())),
                        context=data[section['raw']+m.start()-8:section['raw']+m.start()+20].hex())
                   for m in re.finditer(re.escape(struct.pack('<I', target)), code)]
        references.append(dict(target=hex(target), matches=matches))
print(json.dumps(dict(sha256=digest, image_base=hex(base), references=references), indent=2))
