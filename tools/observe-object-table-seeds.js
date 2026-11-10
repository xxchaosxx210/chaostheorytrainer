// Read-only hypothesis check, not a production object resolver.
// Scan only the installed PE's .data globals once, then sample possible pointer
// arrays. Does not enumerate the heap, call game functions, hook, or write.
const m = Process.mainModule;
if (Process.arch !== 'ia32' || m.name.toLowerCase() !== 'splintercell3.exe') throw new Error('Unexpected target');
const words = new Uint32Array(m.base.add(8970240).readByteArray(1713032));
const candidates = [];
let checked = 0;
let capped = false;
const lower = m.base.toUInt32(), upper = lower + m.size;
for (let i = 0; i < words.length - 2; i++) {
    const address = words[i], count = words[i+1], capacity = words[i+2];
    if (address < 0x10000 || (address & 3) || count < 10000 || count > 250000 || capacity < count || capacity > count*2+1024) continue;
    if (++checked > 200) { capped = true; break; }
    let valid = 0;
    const samples = [];
    for (const index of [0, 1, 2, Math.floor(count/3), Math.floor(count/2), count-2, count-1]) {
        try {
            const object = ptr(address).add(index*4).readPointer();
            if (object.isNull()) continue;
            const vtable = object.readPointer().toUInt32();
            const slot = object.add(4).readU32();
            const name = object.add(0x20).readU32();
            if (vtable < lower || vtable >= upper || slot !== index || name >= 100000) break;
            valid++;
            samples.push({index,object:object.toString(),vtable:ptr(vtable).toString(),name});
        } catch (_) { break; }
    }
    if (valid >= 3) candidates.push({globalRva:'0x'+(8970240+i*4).toString(16),array:ptr(address).toString(),count,capacity,samples});
}
send({kind:'object-table-hypotheses',checked,capped,candidates});
