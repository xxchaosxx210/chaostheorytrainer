// Temporary observer only: preserves arguments and return value, removes its
// sole interceptor after the first call or 60 seconds. No object dereferences.
const m=Process.mainModule;
if(Process.arch!=='ia32'||m.name.toLowerCase()!=='splintercell3.exe')throw new Error('Unexpected target');
const user32=Process.getModuleByName('user32.dll');
const getWindowThread=new NativeFunction(user32.getExportByName('GetWindowThreadProcessId'),'uint',['pointer','pointer'],'stdcall');
const outPid=Memory.alloc(4);
const windowThread=getWindowThread(ptr(463146),outPid);
if(outPid.readU32()!==Process.id||windowThread===0)throw new Error('Window process mismatch');
const target=m.base.add(0x321680);
const prefix=Array.from(new Uint8Array(target.readByteArray(6)));
if(prefix.join(',')!=='81,86,87,106,0,141')throw new Error('Health wrapper prologue mismatch');
let listener=Interceptor.attach(target,{onEnter(){
    const tid=Process.getCurrentThreadId();
    send({kind:'health-execution-thread',windowThread,thread:tid,matched:tid===windowThread,actor:this.context.ecx.toString()});
    if(listener!==null){listener.detach();listener=null;}
}});
setTimeout(()=>{if(listener!==null){listener.detach();listener=null;send({kind:'health-thread-timeout',windowThread});}},60000);
send({kind:'health-thread-observer-armed',windowThread});
