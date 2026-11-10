// Read-only diagnostic. These RVAs are immediate string references found in the
// SHA-256-identified installed executable, not cheat addresses or hook sites.
// Intended for a short Frida attach; no interceptors, timers or game writes.
const m=Process.mainModule; const starts=[0x13c27b,0x13bbd3,0x1416b6,0x2ce21d]; const rows=starts.map(rva=>{let p=m.base.add(rva);const instructions=[];for(let i=0;i<35;i++){try{const op=Instruction.parse(p);instructions.push({address:p.toString(),instruction:op.toString()});p=op.next;}catch(e){instructions.push({error:String(e)});break;}}return {rva:'0x'+rva.toString(16),instructions};});send({kind:'health-string-crossref-instructions',module:m.name,base:m.base.toString(),rows});
