// One-time guarded metadata sample. Read-only, with no production lifetime claims.
const m = Process.mainModule;
if (Process.arch !== 'ia32' || m.name.toLowerCase() !== 'splintercell3.exe') throw new Error('Unexpected target');
const objectsHeader = m.base.add(0xa12084), namesHeader = m.base.add(0xa0dfc0);
const objects = objectsHeader.readPointer(), objectCount = objectsHeader.add(4).readU32();
const names = namesHeader.readPointer(), nameCount = namesHeader.add(4).readU32();
if (objectCount > 100000 || nameCount > 100000) throw new Error('Diagnostic bound exceeded');
function nameOfIndex(index) {
    if (index >= nameCount) return '<invalid-name-index>';
    const entry = names.add(index*4).readPointer();
    if (entry.readU32() !== index) return '<name-index-mismatch>';
    const bytes = new Uint8Array(entry.add(12).readByteArray(128)), end = bytes.indexOf(0);
    return end < 0 ? '<over-bound>' : String.fromCharCode(...bytes.subarray(0,end));
}
function describe(object) {
    if (object.isNull()) return null;
    const index = object.add(4).readU32();
    if (index >= objectCount || !objects.add(index*4).readPointer().equals(object)) return null;
    return {address:object.toString(),index,name:nameOfIndex(object.add(0x20).readU32())};
}
const selected = new Set([3155,12677,11262,5115]);
const snapshot = new Uint32Array(objects.readByteArray(objectCount*4));
const matches=[];
let failures=0;
const start=Date.now();
for (let index=0;index<objectCount;index++) {
    const object=ptr(snapshot[index]);
    if (object.isNull()) continue;
    try {
        if (object.add(4).readU32()!==index || !objects.add(index*4).readPointer().equals(object)) continue;
        if (!selected.has(object.add(0x20).readU32())) continue;
        const identity=describe(object), type=describe(object.add(0x24).readPointer()), at18=describe(object.add(0x18).readPointer());
        const raw=[];
        for(let offset=0x28;offset<0x80;offset+=4) raw.push({offset:'0x'+offset.toString(16),value:'0x'+object.add(offset).readU32().toString(16)});
        matches.push({identity,type,at18,raw,slotUnchanged:objects.add(index*4).readPointer().equals(object)});
    } catch (_) {failures++;}
}
send({kind:'health-property-metadata',objectCount,elapsedMs:Date.now()-start,failures,matches});
