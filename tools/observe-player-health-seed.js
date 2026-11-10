// One-time read-only diagnostic for this installed build/session. No hooks,
// timers, game function calls or writes. Never use these sampled pointers as
// production cached references; they have no game-thread lifetime guarantee.
const m = Process.mainModule;
if (Process.arch !== 'ia32' || m.name.toLowerCase() !== 'splintercell3.exe') throw new Error('Unexpected target');
const start = Date.now();
const tableHeader = m.base.add(0xa12084);
const array = tableHeader.readPointer();
const count = tableHeader.add(4).readU32();
if (count < 1 || count > 100000) throw new Error('Object count outside diagnostic bound');
const snapshot = new Uint32Array(array.readByteArray(count*4));
const matches = [];
let failures = 0;
for (let index = 0; index < count; index++) {
    const object = ptr(snapshot[index]);
    if (object.isNull()) continue;
    try {
        if (!array.add(index*4).readPointer().equals(object) || object.add(4).readU32() !== index) continue;
        const name = object.add(0x20).readU32();
        if (name !== 57183) continue; // EPlayerController1 from this session's names.
        const type = object.add(0x24).readPointer();
        const classNameIndex = type.add(0x20).readU32();
        const pawn = object.add(0x4a0).readPointer(); // Native HEALTH console parser lead.
        const pawnIndex = pawn.add(4).readU32();
        if (pawnIndex >= count || !array.add(pawnIndex*4).readPointer().equals(pawn)) continue;
        const pawnClass = pawn.add(0x24).readPointer();
        const healthBits = pawn.add(0x420).readU32();
        const healthFloat = pawn.add(0x420).readFloat();
        const consistent = array.add(index*4).readPointer().equals(object) &&
            array.add(pawnIndex*4).readPointer().equals(pawn) && object.add(0x4a0).readPointer().equals(pawn);
        matches.push({controller:object.toString(),index,name,classNameIndex,pawn:pawn.toString(),pawnIndex,
            pawnNameIndex:pawn.add(0x20).readU32(),pawnClassNameIndex:pawnClass.add(0x20).readU32(),
            healthBits,healthFloat,consistent});
    } catch (_) { failures++; }
}
send({kind:'player-health-candidate',count,elapsedMs:Date.now()-start,failures,matches,
    headerUnchanged:tableHeader.readPointer().equals(array)&&tableHeader.add(4).readU32()===count});
