#pragma once
#include <windows.h>
#include <cstdint>
#include <cwchar>

namespace CTHealth {
constexpr DWORD Version = 1;
constexpr wchar_t MessageName[] = L"ChaosTheoryTrainer.Health.v1";
struct Request {
    std::uint32_t version, pid, window, requestId, command, result;
    std::uint32_t enabled, ready, gameThread, health, restorePending, reason;
};
static_assert(sizeof(Request) == 48, "Protocol layout must match C#.");
enum Command : DWORD { Probe=1, Enable=2, Disable=3, Status=4, Shutdown=5 };
enum Result : DWORD {
    Success=1, InvalidRequest=2, WrongThread=3, IncompatibleImage=4,
    InvalidTables=5, MetadataMismatch=6, NoMission=7, MultipleControllers=8,
    RestorePending=9, AccessFault=10, TimerFailure=11, PinFailure=12,
    SubclassFailure=13, InvalidCommand=14, NotInitialized=15, WrongWindow=16,
    OwnerUnavailable=17
};
enum Reason : DWORD { Normal=0, ObjectChanged=1, ExternalChange=2, WindowDestroyed=3, Fault=4, OwnerExited=5 };
inline void MappingName(wchar_t (&name)[96], DWORD pid) {
    swprintf_s(name, L"Local\\ChaosTheoryTrainer.Health.v1.%lu", pid);
}
}
