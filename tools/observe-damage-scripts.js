// Read-only snapshot of selected reflected function metadata and script bytes.
// No game calls, hooks, writes or timers. Results require VM interpretation.
const m=Process.mainModule, oh=m.base.add(0xa12084), nh=m.base.add(0xa0dfc0);
if(Process.arch!=='ia32'||m.name.toLowerCase()!=='splintercell3.exe')throw new Error('Unexpected target');
const objects=oh.readPointer(), count=oh.add(4).readU32(), names=nh.readPointer(), nc=nh.add(4).readU32();
if(count>100000||nc>100000)throw new Error('Diagnostic bound exceeded');
function name(index){if(index>=nc)return'?';const p=names.add(index*4).readPointer();if(p.readU32()!==index)return'?';const a=new Uint8Array(p.add(12).readByteArray(128)),z=a.indexOf(0);return z<0?'?':String.fromCharCode(...a.subarray(0,z));}
function valid(p){if(p.isNull())return false;const i=p.add(4).readU32();return i<count&&objects.add(i*4).readPointer().equals(p);}
const snapshot=new Uint32Array(objects.readByteArray(count*4)), selected=new Set([1642,3163,9950,3155]);
const owners=new Set(['Pawn','EPawn','ESam','EPlayerController']);const functions=[];let failures=0;
for(let i=0;i<count;i++)try{
    const p=ptr(snapshot[i]);if(p.isNull()||!valid(p)||!selected.has(p.add(0x20).readU32()))continue;
    const type=p.add(0x24).readPointer(), owner=p.add(0x18).readPointer();
    if(!valid(type)||!valid(owner)||name(type.add(0x20).readU32())!=='Function')continue;
    const ownerName=name(owner.add(0x20).readU32());if(!owners.has(ownerName))continue;
    const script=p.add(0x50).readPointer(), size=p.add(0x54).readU32(),capacity=p.add(0x58).readU32();
    if(size>16384||capacity<size)throw new Error('Script bound exceeded');
    functions.push({address:p.toString(),name:name(p.add(0x20).readU32()),owner:ownerName,script:script.toString(),size,
        flags:p.add(0x6c).readU32(),native:p.add(0x78).readPointer().toString(),
        bytes:size?Array.from(new Uint8Array(script.readByteArray(size))):[],slotUnchanged:valid(p)});
}catch(_){failures++;}
send({kind:'damage-script-snapshot',count,failures,functions});
