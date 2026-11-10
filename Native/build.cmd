@echo off
setlocal
call "C:\Program Files\Microsoft Visual Studio\2022\Community\VC\Auxiliary\Build\vcvars32.bat" >nul
if errorlevel 1 exit /b 1
pushd "%~dp0"
if errorlevel 1 exit /b 1
if not exist out mkdir out
cl /nologo /W4 /WX /EHsc /O2 /MT /DUNICODE /D_UNICODE /DCT_HEALTH_DLL /LD HealthBridge.cpp /Foout\bridge.obj /Feout\CTHealthBridge.dll /link user32.lib comctl32.lib
if errorlevel 1 goto failed
cl /nologo /W4 /WX /EHsc /O2 /MT /DUNICODE /D_UNICODE HealthBridge.cpp /Foout\host.obj /Feout\CTHealthHost.exe /link user32.lib bcrypt.lib
if errorlevel 1 goto failed
cl /nologo /W4 /WX /EHsc /O2 /MT /DUNICODE /D_UNICODE /DCT_HEALTH_DLL /DCT_HEALTH_POLICY_TESTS HealthBridge.cpp /Foout\policy.obj /Feout\CTHealthPolicyTests.exe /link user32.lib comctl32.lib
if errorlevel 1 goto failed
popd
exit /b 0
:failed
popd
exit /b 1
