#include "HealthProtocol.h"
#include <cstdio>
#include <cstring>
#include <cstdlib>

static_assert(sizeof(void*) == 4, "Build this bridge and host with the x86 toolchain.");
using Byte = unsigned char;

#ifdef CT_HEALTH_DLL
#include <commctrl.h>
using namespace CTHealth;
namespace {
constexpr DWORD ObjectsRva=0xa12084, NamesRva=0xa0dfc0;
constexpr DWORD ExpectedImageSize=11177984;
constexpr UINT_PTR SubclassId=0x43544831, TimerId=0x43544832;
struct Table { Byte** data; DWORD count, capacity; };
struct Ref { Byte* pointer; DWORD index, name; Byte* type; Byte* outer; };
struct Candidate { Ref controller, pawn, world; DWORD original; };
Byte* image=nullptr;
HWND gameWindow=nullptr;
DWORD gameThread=0;
bool installed=false, active=false, ready=false, pending=false, applied=false;
DWORD lastReason=Normal;
UINT_PTR timer=0;
Candidate current={};
HANDLE ownerProcess=nullptr;
DWORD deleteOffset=0,deleteMask=0;

DWORD U(const Byte* p, DWORD offset=0) { return *reinterpret_cast<const DWORD*>(p+offset); }
Byte* P(const Byte* p, DWORD offset) { return *reinterpret_cast<Byte* const*>(p+offset); }
Table Objects() { return *reinterpret_cast<Table*>(image+ObjectsRva); }
Table Names() { return *reinterpret_cast<Table*>(image+NamesRva); }
bool ValidTable(const Table& t) {
    return t.data && t.count>0 && t.count<=100000 && t.capacity>=t.count && t.capacity<=1000000;
}
bool Slot(const Table& t, const Ref& ref) {
    // Do not derive an index from a cached pointer: validate the independent slot first.
    return ref.pointer && ref.index<t.count && t.data[ref.index]==ref.pointer;
}
bool Identity(const Table& t, const Ref& ref) {
    return Slot(t,ref) && U(ref.pointer,4)==ref.index && U(ref.pointer,0x20)==ref.name && P(ref.pointer,0x24)==ref.type;
}
bool Capture(const Table& t, Byte* p, Ref& ref) {
    if(!p)return false;
    DWORD index=U(p,4);
    if(index>=t.count || t.data[index]!=p)return false;
    ref={p,index,U(p,0x20),P(p,0x24),P(p,0x18)};
    return true;
}
bool Named(const Table& names, DWORD index, const char* expected) {
    if(index>=names.count)return false;
    Byte* entry=names.data[index];
    if(!entry || U(entry)!=index)return false;
    const char* text=reinterpret_cast<const char*>(entry+12);
    for(size_t i=0;i<96;++i) {
        if(text[i]!=expected[i])return false;
        if(!expected[i])return true;
    }
    return false;
}
bool ObjectNamed(const Table& objects,const Table& names,Byte* p,const char* expected) {
    Ref ref={};return Capture(objects,p,ref)&&Named(names,ref.name,expected);
}
bool ControllerLive(const Table& objects) {
    return Identity(objects,current.controller) && deleteMask && (U(current.controller.pointer,deleteOffset)&deleteMask)==0;
}
bool SessionLive(const Table& objects) {
    // Pawn and world checks touch only table entries and pointer values in the live
    // controller. They never dereference the cached pawn/world after destruction.
    return ControllerLive(objects) && Slot(objects,current.world) && Slot(objects,current.pawn) &&
        P(current.controller.pointer,0x18)==current.controller.outer &&
        P(current.controller.pointer,0x4a0)==current.pawn.pointer;
}
void KillHealthTimer() {
    if(timer){KillTimer(gameWindow,timer);timer=0;}
}
void CloseOwner(){if(ownerProcess){CloseHandle(ownerProcess);ownerProcess=nullptr;}}
void Discard() {
    active=false;ready=false;pending=false;applied=false;current={};CloseOwner();
}
bool Restore() {
    active=false;
    // Probe owns no change. Shutdown after Probe must not touch actor flags,
    // even if the game enabled invincibility after that read-only snapshot.
    if(!applied){Discard();return true;}
    if(!current.controller.pointer){pending=true;return false;}
    Table objects=Objects();
    if(!ValidTable(objects)){pending=true;return false;}
    if(!ControllerLive(objects)){Discard();return true;}
    auto flags=reinterpret_cast<volatile LONG*>(current.controller.pointer+0x554);
    // A game-side clear is not ours to undo. Preserve all unrelated bits.
    if((*flags&0x100000)!=0 && current.original==0)InterlockedAnd(flags,~static_cast<LONG>(0x100000));
    pending=((*flags&0x100000)!=0 && current.original==0);
    if(!pending){current={};ready=false;applied=false;CloseOwner();}
    return !pending;
}
DWORD ValidateCached() {
    if(!current.controller.pointer){ready=false;return Success;}
    Table objects=Objects();
    if(!ValidTable(objects)){active=false;ready=false;pending=applied;KillHealthTimer();return InvalidTables;}
    if(!SessionLive(objects)){
        active=false;ready=false;lastReason=ObjectChanged;KillHealthTimer();
        return Restore()?Success:RestorePending;
    }
    if(active && (U(current.controller.pointer,0x554)&0x100000)==0){
        // Do not repeatedly fight scripted game-side changes to the health flag.
        lastReason=ExternalChange;KillHealthTimer();Discard();
    }
    return Success;
}
bool ImageMatches() {
    image=reinterpret_cast<Byte*>(GetModuleHandleW(nullptr));
    if(!image)return false;
    auto dos=reinterpret_cast<IMAGE_DOS_HEADER*>(image);
    if(dos->e_magic!=IMAGE_DOS_SIGNATURE || dos->e_lfanew<=0 || dos->e_lfanew>0x1000)return false;
    auto nt=reinterpret_cast<IMAGE_NT_HEADERS32*>(image+dos->e_lfanew);
    if(nt->Signature!=IMAGE_NT_SIGNATURE || nt->FileHeader.Machine!=IMAGE_FILE_MACHINE_I386 ||
       nt->OptionalHeader.Magic!=IMAGE_NT_OPTIONAL_HDR32_MAGIC || nt->OptionalHeader.SizeOfImage!=ExpectedImageSize)return false;
    // Actual installed GetHealthPCT wrapper bytes observed during investigation.
    const Byte prefix[]={0x51,0x56,0x57,0x6a,0x00,0x8d,0x44,0x24,0x0c,0x8b,0xf1};
    return memcmp(image+0x321680,prefix,sizeof(prefix))==0;
}
DWORD Resolve(Candidate& found, DWORD& health) {
    Table objects=Objects(),names=Names();
    if(!ValidTable(objects)||!ValidTable(names))return InvalidTables;
    bool invincible=false,pawnField=false,healthField=false,isPlayerField=false,deleteField=false;
    Byte* invincibleProperty=nullptr;Byte* samDamageFunction=nullptr;
    Ref controllerSeeds[16]={};DWORD seedCount=0;
    DWORD resolvedDeleteOffset=0,resolvedDeleteMask=0;
    DWORD controllers=0;
    for(DWORD i=0;i<objects.count;++i){
        Byte* object=objects.data[i];if(!object)continue;
        Ref ref={};if(!Capture(objects,object,ref)||ref.index!=i)continue;
        Ref type={};if(!Capture(objects,ref.type,type))continue;
        if(Named(names,type.name,"Function") && Named(names,ref.name,"TakeDamage") && ObjectNamed(objects,names,ref.outer,"ESam")){
            if(samDamageFunction && samDamageFunction!=object)return MetadataMismatch;
            samDamageFunction=object;
        }
        if(Named(names,type.name,"EPlayerController")){
            if(seedCount>=_countof(controllerSeeds))return MultipleControllers;
            controllerSeeds[seedCount++]=ref;
        }
        const bool isBool=Named(names,type.name,"BoolProperty");
        const bool isObject=Named(names,type.name,"ObjectProperty");
        const bool isInt=Named(names,type.name,"IntProperty");
        if(!isBool&&!isObject&&!isInt)continue;
        if((U(object,0x34)&0xffff)!=1)continue;
        if(isBool && Named(names,ref.name,"bInvincible") && ObjectNamed(objects,names,ref.outer,"EPlayerController")){
            if(invincibleProperty && invincibleProperty!=object)return MetadataMismatch;
            invincible=U(object,0x44)==0x554&&U(object,0x54)==0x100000;invincibleProperty=object;
        }
        if(isObject && Named(names,ref.name,"EPawn") && ObjectNamed(objects,names,ref.outer,"EPlayerController"))
            pawnField=U(object,0x44)==0x4a0;
        if(isInt && Named(names,ref.name,"Health") && ObjectNamed(objects,names,ref.outer,"Pawn"))
            healthField=U(object,0x44)==0x420;
        if(isBool && Named(names,ref.name,"bIsPlayerPawn") && ObjectNamed(objects,names,ref.outer,"Actor"))
            isPlayerField=U(object,0x44)==0x2c&&U(object,0x54)==0x20;
        if(isBool && Named(names,ref.name,"bDeleteMe") && ObjectNamed(objects,names,ref.outer,"Actor")){
            resolvedDeleteOffset=U(object,0x44);resolvedDeleteMask=U(object,0x54);
            deleteField=resolvedDeleteOffset>=0x28&&resolvedDeleteOffset<=0x100&&resolvedDeleteOffset%4==0&&
                resolvedDeleteMask!=0&&(resolvedDeleteMask&(resolvedDeleteMask-1))==0;
        }
    }
    if(!invincible||!pawnField||!healthField||!isPlayerField||!deleteField)return MetadataMismatch;
    if(!samDamageFunction || !invincibleProperty)return MetadataMismatch;
    Byte* script=P(samDamageFunction,0x50);DWORD scriptSize=U(samDamageFunction,0x54),scriptCapacity=U(samDamageFunction,0x58);
    if(!script || scriptSize<0x71 || scriptSize>16384 || scriptCapacity<scriptSize || scriptCapacity>1000000)return MetadataMismatch;
    // Freshly observed ESam early return: if (... || Controller.bInvincible ||
    // ...) return. Reject changed .u scripts before any health write.
    if(script[0x1a]!=0x07 || *reinterpret_cast<WORD*>(script+0x1b)!=0x71 ||
       script[0x1d]!=0x84 || script[0x1e]!=0x84 || script[0x1f]!=0x84 ||
       script[0x39]!=0x2d || script[0x3a]!=0x01 || P(script,0x3b)!=invincibleProperty ||
       script[0x6f]!=0x04 || script[0x70]!=0x0b)return MetadataMismatch;
    deleteOffset=resolvedDeleteOffset;deleteMask=resolvedDeleteMask;
    // Actor layout reads begin only after all required reflected fields validate.
    for(DWORD i=0;i<seedCount;++i){
        const Ref& ref=controllerSeeds[i];Byte* object=ref.pointer;
        if(!Identity(objects,ref)||(U(object,deleteOffset)&deleteMask)!=0)continue;
        Byte* pawn=P(object,0x4a0);Ref pawnRef={};
        if(!Capture(objects,pawn,pawnRef))continue;
        if(!ObjectNamed(objects,names,pawnRef.type,"ESam") || (U(pawn,0x2c)&0x20)==0 || (U(pawn,deleteOffset)&deleteMask)!=0)continue;
        if(static_cast<LONG>(U(pawn,0x420))<=0)continue;
        Ref world={};if(!Capture(objects,ref.outer,world))continue;
        ++controllers;found={ref,pawnRef,world,U(object,0x554)&0x100000};health=U(pawn,0x420);
    }
    if(controllers==0)return NoMission;
    if(controllers!=1)return MultipleControllers;
    return Success;
}
void CALLBACK HealthTimer(HWND window,UINT,UINT_PTR, DWORD) {
    if(window!=gameWindow || GetCurrentThreadId()!=gameThread)return;
    __try {
        if(ownerProcess && WaitForSingleObject(ownerProcess,0)!=WAIT_TIMEOUT){
            active=false;KillHealthTimer();lastReason=OwnerExited;Restore();CloseOwner();
        } else ValidateCached();
    }
    __except(EXCEPTION_EXECUTE_HANDLER) {
        active=false;ready=false;pending=applied;lastReason=Fault;KillHealthTimer();
    }
}
DWORD Execute(Request& request) {
    request.health=0;
    if(GetCurrentThreadId()!=gameThread)return WrongThread;
    if(request.command==Shutdown || request.command==Disable) {
        KillHealthTimer();active=false;
        if(!Restore())return RestorePending;
        lastReason=Normal;return Success;
    }
    if(request.command==Status){DWORD result=ValidateCached();return result==Success&&pending?RestorePending:result;}
    if(request.command!=Probe && request.command!=Enable)return InvalidCommand;
    DWORD validation=ValidateCached();if(validation!=Success)return validation;
    if(active){request.health=0;return Success;}
    if(pending)return RestorePending;
    Candidate found={};DWORD health=0;
    DWORD result=Resolve(found,health);
    if(result!=Success){ready=false;return result;}
    current=found;ready=true;lastReason=Normal;request.health=health;
    if(request.command==Probe)return Success;
    // Input restorePending carries the requesting trainer PID on Enable only;
    // the response overwrites it with the restoration state.
    const DWORD ownerPid=request.restorePending;
    if(!ownerPid || ownerPid==GetCurrentProcessId()){Discard();return OwnerUnavailable;}
    ownerProcess=OpenProcess(SYNCHRONIZE,FALSE,ownerPid);
    if(!ownerProcess || WaitForSingleObject(ownerProcess,0)!=WAIT_TIMEOUT){Discard();return OwnerUnavailable;}
    // Install the monitor before writing so timer failure cannot leave an enabled bit.
    timer=SetTimer(gameWindow,TimerId,1000,HealthTimer);
    if(!timer){Discard();return TimerFailure;}
    auto flags=reinterpret_cast<volatile LONG*>(current.controller.pointer+0x554);
    // From here a mutation might have occurred even if the following readback
    // faults. Preserve restoration responsibility until acknowledged cleanup.
    applied=true;
    LONG previous=InterlockedOr(flags,static_cast<LONG>(0x100000));
    current.original=static_cast<DWORD>(previous)&0x100000;
    if((*flags&0x100000)==0){KillHealthTimer();Discard();return AccessFault;}
    active=true;return Success;
}
LRESULT CALLBACK HealthSubclass(HWND,UINT,WPARAM,LPARAM,UINT_PTR,DWORD_PTR);
void ProcessRequest(HWND window,WPARAM nonce) {
    wchar_t name[96];MappingName(name,GetCurrentProcessId());
    HANDLE mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,name);if(!mapping)return;
    auto request=static_cast<Request*>(MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(Request)));
    if(!request){CloseHandle(mapping);return;}
    if(request->result==0 && request->requestId==static_cast<DWORD>(nonce)) {
        if(request->version!=Version || request->pid!=GetCurrentProcessId() || request->window!=reinterpret_cast<DWORD>(window))request->result=InvalidRequest;
        else if(GetCurrentThreadId()!=gameThread)request->result=WrongThread;
        else {
            __try {request->result=Execute(*request);}
            __except(EXCEPTION_EXECUTE_HANDLER){active=false;ready=false;pending=applied;lastReason=Fault;KillHealthTimer();request->result=AccessFault;}
            request->enabled=active?1:0;request->ready=ready?1:0;request->gameThread=gameThread;
            request->restorePending=pending?1:0;request->reason=lastReason;
            if(request->command==Shutdown && request->result==Success){
                RemoveWindowSubclass(window,HealthSubclass,SubclassId);installed=false;
            }
        }
    }
    UnmapViewOfFile(request);CloseHandle(mapping);
}
LRESULT CALLBACK HealthSubclass(HWND window,UINT message,WPARAM wp,LPARAM lp,UINT_PTR,DWORD_PTR) {
    if(message==WM_NCDESTROY) {
        // Teardown must not resolve, read or restore any game object.
        active=false;KillHealthTimer();Discard();lastReason=WindowDestroyed;
        RemoveWindowSubclass(window,HealthSubclass,SubclassId);installed=false;
        return DefSubclassProc(window,message,wp,lp);
    }
    if(message==RegisterWindowMessageW(MessageName)){ProcessRequest(window,wp);return 0;}
    return DefSubclassProc(window,message,wp,lp);
}
void BootstrapError(HWND window,WPARAM nonce,DWORD result){
    wchar_t name[96];MappingName(name,GetCurrentProcessId());
    HANDLE mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,name);if(!mapping)return;
    auto request=static_cast<Request*>(MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(Request)));
    if(request){
        if(request->result==0 && request->requestId==static_cast<DWORD>(nonce) && request->version==Version &&
            request->pid==GetCurrentProcessId() && request->window==reinterpret_cast<DWORD>(window)){
            request->result=result;request->gameThread=GetCurrentThreadId();request->enabled=active?1:0;
            request->ready=ready?1:0;request->restorePending=pending?1:0;request->reason=lastReason;
        }
        UnmapViewOfFile(request);
    }
    CloseHandle(mapping);
}
}
extern "C" __declspec(dllexport) LRESULT CALLBACK HealthBootstrap(int code,WPARAM wp,LPARAM lp) {
    if(code>=0){
        auto message=reinterpret_cast<CWPSTRUCT*>(lp);
        if(message->message==RegisterWindowMessageW(MessageName)){
            HMODULE self=nullptr;
            DWORD windowPid=0;const DWORD thread=GetWindowThreadProcessId(message->hwnd,&windowPid);
            if(windowPid!=GetCurrentProcessId())BootstrapError(message->hwnd,message->wParam,WrongWindow);
            else if(GetCurrentThreadId()!=thread)BootstrapError(message->hwnd,message->wParam,WrongThread);
            else if(!GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS|GET_MODULE_HANDLE_EX_FLAG_PIN,
                reinterpret_cast<LPCWSTR>(&HealthBootstrap),&self))BootstrapError(message->hwnd,message->wParam,PinFailure);
            else {
                bool matches=false;
                __try {matches=ImageMatches();} __except(EXCEPTION_EXECUTE_HANDLER){matches=false;}
                if(matches){
                    gameThread=thread;gameWindow=message->hwnd;
                    if(installed || SetWindowSubclass(gameWindow,HealthSubclass,SubclassId,0)){
                        installed=true;ProcessRequest(gameWindow,message->wParam);
                    } else BootstrapError(message->hwnd,message->wParam,SubclassFailure);
                } else BootstrapError(message->hwnd,message->wParam,IncompatibleImage);
            }
        }
    }
    return CallNextHookEx(nullptr,code,wp,lp);
}
BOOL WINAPI DllMain(HINSTANCE,DWORD,LPVOID){return TRUE;}

#ifdef CT_HEALTH_POLICY_TESTS
namespace {
void Check(bool condition,const char* description){
    if(!condition){printf("FAIL: %s\n",description);ExitProcess(1);}
    printf("PASS: %s\n",description);
}
Byte* testController=nullptr;
Byte* inaccessible=nullptr;
Byte* slots[4]={};
void PreparePolicyCase(){
    ZeroMemory(testController,4096);ZeroMemory(slots,sizeof(slots));
    slots[1]=testController;slots[2]=inaccessible;slots[3]=inaccessible;
    *reinterpret_cast<Table*>(image+ObjectsRva)={slots,4,4};
    *reinterpret_cast<DWORD*>(testController+4)=1;
    *reinterpret_cast<DWORD*>(testController+0x20)=100;
    *reinterpret_cast<Byte**>(testController+0x24)=reinterpret_cast<Byte*>(0x12345678);
    *reinterpret_cast<Byte**>(testController+0x18)=inaccessible;
    *reinterpret_cast<Byte**>(testController+0x4a0)=inaccessible;
    *reinterpret_cast<DWORD*>(testController+0x554)=0x80100008;
    current={{testController,1,100,reinterpret_cast<Byte*>(0x12345678),inaccessible},
        {inaccessible,2,0,nullptr,nullptr},{inaccessible,3,0,nullptr,nullptr},0};
    deleteOffset=0x2c;deleteMask=0x10;
    active=true;ready=true;pending=false;applied=true;lastReason=Normal;timer=0;gameWindow=nullptr;gameThread=GetCurrentThreadId();
}
}
int main(){
    image=static_cast<Byte*>(VirtualAlloc(nullptr,ExpectedImageSize,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
    testController=static_cast<Byte*>(VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_READWRITE));
    inaccessible=static_cast<Byte*>(VirtualAlloc(nullptr,4096,MEM_RESERVE|MEM_COMMIT,PAGE_NOACCESS));
    Check(image&&testController&&inaccessible,"Synthetic test memory allocated; no game attached");
    PreparePolicyCase();
    Check(SessionLive(Objects()),"Session validation never dereferences cached pawn or world");
    slots[1]=nullptr;current.controller.pointer=inaccessible;
    Check(!ControllerLive(Objects()),"Cached controller slot is checked before reading an inaccessible controller");
    PreparePolicyCase();slots[2]=nullptr;
    Check(!SessionLive(Objects()),"Freed pawn slot invalidates session without touching pawn memory");
    Check(Restore(),"OFF restoration succeeds after cached pawn memory becomes inaccessible");
    Check(U(testController,0x554)==0x80000008,"OFF clears only the owned health mask and preserves unrelated bits");
    PreparePolicyCase();current.original=0x100000;
    Check(Restore()&&U(testController,0x554)==0x80100008,"OFF preserves an originally enabled game health flag");
    PreparePolicyCase();applied=false;current.controller.pointer=inaccessible;slots[1]=nullptr;
    Check(Restore()&&!pending,"Shutdown after read-only Probe does not read actor memory or claim a restoration debt");
    PreparePolicyCase();applied=false;active=false;
    Check(Restore()&&U(testController,0x554)==0x80100008,"Read-only Probe never owns or clears a later game-set invincibility bit");
    PreparePolicyCase();*reinterpret_cast<DWORD*>(testController+0x554)=0x80000008;
    Check(ValidateCached()==Success&&!active&&!ready&&lastReason==ExternalChange,"Game-side clear ends trainer ownership without reapplying the flag");
    PreparePolicyCase();*reinterpret_cast<Byte**>(testController+0x18)=nullptr;
    Check(ValidateCached()==Success&&!active&&lastReason==ObjectChanged,"World change auto-disables using only independently valid controller access");
    PreparePolicyCase();slots[1]=nullptr;current.controller.pointer=inaccessible;
    Check(Restore()&&!pending,"Shutdown discards freed controller without dereferencing it");
    PreparePolicyCase();*reinterpret_cast<DWORD*>(testController+deleteOffset)=deleteMask;
    Check(Restore()&&U(testController,0x554)==0x80100008,"Pending-delete controller is discarded without writing health state");
    PreparePolicyCase();
    // A signaled synchronization handle exercises the same timer branch used by
    // an exited owner process; no real trainer/game processes are opened here.
    ownerProcess=CreateEventW(nullptr,TRUE,TRUE,nullptr);
    Check(ownerProcess!=nullptr,"Synthetic signaled owner handle created");
    HealthTimer(nullptr,0,0,0);
    Check(!active&&!pending&&lastReason==OwnerExited&&U(testController,0x554)==0x80000008&&ownerProcess==nullptr,
        "Owner-exit timer path restores health and releases its handle");
    VirtualFree(inaccessible,0,MEM_RELEASE);VirtualFree(testController,0,MEM_RELEASE);VirtualFree(image,0,MEM_RELEASE);
    puts("Native health policy checks passed; no gameplay behavior was tested.");return 0;
}
#endif

#else
#include <bcrypt.h>
using namespace CTHealth;
namespace {
bool HashMatches(const wchar_t* path) {
    const Byte expected[32]={0x54,0xdf,0x25,0xb2,0x38,0x35,0x60,0x24,0x24,0x80,0x85,0xca,0x7f,0xa6,0xaf,0x90,
        0x05,0xb0,0xbe,0x4e,0xc4,0x0b,0xee,0xf3,0xc3,0x2f,0xaa,0x8c,0x06,0xb6,0xd4,0xe5};
    HANDLE file=CreateFileW(path,GENERIC_READ,FILE_SHARE_READ,nullptr,OPEN_EXISTING,FILE_ATTRIBUTE_NORMAL,nullptr);
    if(file==INVALID_HANDLE_VALUE)return false;
    BCRYPT_ALG_HANDLE algorithm=nullptr;BCRYPT_HASH_HANDLE hash=nullptr;Byte output[32],buffer[65536];DWORD read=0;
    bool ok=BCryptOpenAlgorithmProvider(&algorithm,BCRYPT_SHA256_ALGORITHM,nullptr,0)>=0;
    if(ok)ok=BCryptCreateHash(algorithm,&hash,nullptr,0,nullptr,0,0)>=0;
    while(ok){if(!ReadFile(file,buffer,sizeof(buffer),&read,nullptr)){ok=false;break;}if(!read)break;ok=BCryptHashData(hash,buffer,read,0)>=0;}
    if(ok)ok=BCryptFinishHash(hash,output,sizeof(output),0)>=0&&memcmp(output,expected,sizeof(expected))==0;
    if(hash)BCryptDestroyHash(hash);if(algorithm)BCryptCloseAlgorithmProvider(algorithm,0);CloseHandle(file);return ok;
}
bool ParseNumber(const wchar_t* text,DWORD& value) {
    wchar_t* end=nullptr;unsigned long long number=wcstoull(text,&end,10);
    if(!text[0]||!end||*end||number==0||number>0xffffffffULL)return false;
    value=static_cast<DWORD>(number);return true;
}
}
int wmain(int argc,wchar_t** argv) {
    if(argc==2&&!wcscmp(argv[1],L"--version")){puts("CTHealthHost:0.1.0:protocol1:x86");return 0;}
    if(argc==3&&!wcscmp(argv[1],L"--verify-file")){
        const bool supported=HashMatches(argv[2]);
        puts(supported?"CTHEALTH:1:supported-file-fingerprint":"CTHEALTH:unsupported-file-fingerprint");return supported?0:5;
    }
    DWORD pid=0,windowNumber=0,nonce=0;
    if(argc!=4||!ParseNumber(argv[1],pid)||!ParseNumber(argv[2],windowNumber)||!ParseNumber(argv[3],nonce)){
        puts("Expected decimal PID, game window handle and nonzero request ID.");return 2;}
    HWND window=reinterpret_cast<HWND>(windowNumber);DWORD owner=0;
    DWORD thread=GetWindowThreadProcessId(window,&owner);
    if(!thread||owner!=pid||!IsWindow(window)||GetWindow(window,GW_OWNER)) {puts("Game window validation failed.");return 3;}
    HANDLE process=OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION,FALSE,pid);
    if(!process){printf("Could not inspect game process (%lu).\n",GetLastError());return 4;}
    wchar_t executable[32768];DWORD length=_countof(executable);
    bool ok=QueryFullProcessImageNameW(process,0,executable,&length)!=FALSE;
    BOOL wow=FALSE;
    if(!IsWow64Process(process,&wow))ok=false;
    SYSTEM_INFO system={};GetNativeSystemInfo(&system);
    if(system.wProcessorArchitecture!=PROCESSOR_ARCHITECTURE_INTEL&&!wow)ok=false;
    CloseHandle(process);
    const wchar_t* slash=ok?wcsrchr(executable,L'\\'):nullptr;
    if(!ok||!slash||_wcsicmp(slash+1,L"splintercell3.exe")||!HashMatches(executable)) {
        puts("Unsupported executable, architecture, file access or SHA-256 fingerprint.");return 5;}
    wchar_t dllPath[32768];DWORD dllLength=GetModuleFileNameW(nullptr,dllPath,_countof(dllPath));
    wchar_t* dllSlash=wcsrchr(dllPath,L'\\');
    if(!dllLength||dllLength>=_countof(dllPath)||!dllSlash)return 6;
    wcscpy_s(dllSlash+1,_countof(dllPath)-(dllSlash+1-dllPath),L"CTHealthBridge.dll");
    HMODULE dll=LoadLibraryExW(dllPath,nullptr,LOAD_WITH_ALTERED_SEARCH_PATH);
    if(!dll){printf("Could not load CTHealthBridge.dll (%lu).\n",GetLastError());return 6;}
    auto hookProc=reinterpret_cast<HOOKPROC>(GetProcAddress(dll,"_HealthBootstrap@12"));
    if(!hookProc){puts("Health bridge export missing.");FreeLibrary(dll);return 6;}
    wchar_t mappingName[96];MappingName(mappingName,pid);
    HANDLE mapping=OpenFileMappingW(FILE_MAP_ALL_ACCESS,FALSE,mappingName);
    if(!mapping){puts("GUI request mapping missing.");FreeLibrary(dll);return 7;}
    auto request=static_cast<Request*>(MapViewOfFile(mapping,FILE_MAP_ALL_ACCESS,0,0,sizeof(Request)));
    if(!request){CloseHandle(mapping);FreeLibrary(dll);return 7;}
    if(request->version!=Version||request->pid!=pid||request->window!=windowNumber||request->requestId!=nonce||request->result!=0){
        puts("GUI request header invalid.");UnmapViewOfFile(request);CloseHandle(mapping);FreeLibrary(dll);return 7;}
    HHOOK hook=SetWindowsHookExW(WH_CALLWNDPROC,hookProc,dll,thread);
    if(!hook){printf("Could not attach window-thread bootstrap (%lu).\n",GetLastError());UnmapViewOfFile(request);CloseHandle(mapping);FreeLibrary(dll);return 8;}
    DWORD_PTR response=0;
    BOOL sent=SendMessageTimeoutW(window,RegisterWindowMessageW(MessageName),nonce,0,SMTO_ABORTIFHUNG|SMTO_BLOCK,5000,&response)!=0;
    UnhookWindowsHookEx(hook);
    DWORD result=request->result;
    UnmapViewOfFile(request);CloseHandle(mapping);FreeLibrary(dll);
    if(!sent){puts("Game-thread request timed out; completion is unknown. Do not assume OFF or retry a toggle automatically.");return 9;}
    if(result!=Success){printf("Health bridge bootstrap/request failed: result %lu.\n",result);return 10;}
    puts("CTHEALTH:1:game-thread-request-completed");return 0;
}
#endif
