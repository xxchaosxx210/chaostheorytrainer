// One bounded read-only traversal of the FName hash buckets observed in the
// installed build's constructor at RVA 0x3d4790. No UObject traversal or writes.
if (Process.arch !== 'ia32' || Process.mainModule.name.toLowerCase() !== 'splintercell3.exe')
    throw new Error('Unexpected target');
const moduleBase = Process.mainModule.base;
const buckets = moduleBase.add(0xa09fb8);
const visited = new Set();
const matches = [];
let failures = 0;
let capped = false;
for (let bucket = 0; bucket < 4096; bucket++) {
    let entry;
    try { entry = buckets.add(bucket * 4).readPointer(); } catch (_) { failures++; continue; }
    while (!entry.isNull()) {
        if (visited.size >= 100000) { capped = true; break; }
        const key = entry.toString();
        if (visited.has(key)) break;
        visited.add(key);
        let next;
        try { next = entry.add(8).readPointer(); } catch (_) { failures++; break; }
        try {
            // Decode bounded raw bytes explicitly; do not let fixed-size string
            // decoding include adjacent allocations in a name match.
            const bytes = new Uint8Array(entry.add(12).readByteArray(128));
            const end = bytes.indexOf(0);
            const name = end < 0 ? '' : String.fromCharCode(...bytes.subarray(0, end));
            if (/health|damage|invinc|invuln|playerpawn|playercontroller|samfisher/i.test(name))
                matches.push({entry:key, header:[entry.readU32(),entry.add(4).readU32()], name});
        } catch (_) { failures++; }
        entry = next;
    }
    if (capped) break;
}
send({kind:'bounded-name-seeds', visited:visited.size, failures, capped, matches:matches.slice(0,160),
    matchesTruncated:matches.length>160,
    takeDamageNameIndex:moduleBase.add(0x8fa154).readU32(),
    getHealthNameIndex:moduleBase.add(0x8fa034).readU32()});
