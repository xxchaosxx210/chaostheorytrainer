$ErrorActionPreference = 'Stop'
$trainerProject = Join-Path $PSScriptRoot 'ChaosTheoryTrainer.csproj'
$trainerNativeBuild = Join-Path $PSScriptRoot 'Native\build.cmd'
if (Test-Path -LiteralPath $trainerNativeBuild) {
    $trainerNativeDirectory = Join-Path $PSScriptRoot 'Native'
    $trainerNativeOutputs = @(
        (Join-Path $trainerNativeDirectory 'out\CTHealthHost.exe'),
        (Join-Path $trainerNativeDirectory 'out\CTHealthBridge.dll')
    )
    $trainerNativeNeedsBuild = @($trainerNativeOutputs | Where-Object { -not (Test-Path -LiteralPath $_) }).Count -gt 0
    if (-not $trainerNativeNeedsBuild) {
        $trainerNativeOldestOutput = ($trainerNativeOutputs | Get-Item | Sort-Object LastWriteTimeUtc | Select-Object -First 1).LastWriteTimeUtc
        $trainerNativeNeedsBuild = @(Get-ChildItem -LiteralPath $trainerNativeDirectory -File |
            Where-Object { $_.Extension -in '.cpp','.h','.cmd' -and $_.LastWriteTimeUtc -gt $trainerNativeOldestOutput }).Count -gt 0
    }
    if ($trainerNativeNeedsBuild) {
        & $trainerNativeBuild
        if ($LASTEXITCODE -ne 0) { throw 'Native health bridge build failed.' }
    }
}
dotnet build $trainerProject -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Trainer build failed.' }
$trainerExecutable = Join-Path $PSScriptRoot 'bin\Release\net7.0-windows\ChaosTheoryTrainer.exe'
Start-Process -FilePath $trainerExecutable -WorkingDirectory $PSScriptRoot -WindowStyle Hidden
