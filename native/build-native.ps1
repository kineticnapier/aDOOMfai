param(
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$build = Join-Path $root "build"

& (Join-Path $root "fetch-doomgeneric.ps1")

$gccCandidates = @(
    "C:\msys64\ucrt64\bin\gcc.exe",
    "C:\msys64\mingw64\bin\gcc.exe",
    "C:\msys64\clang64\bin\clang.exe"
)

$cmakeCandidates = @(
    "C:\msys64\ucrt64\bin\cmake.exe",
    "C:\msys64\mingw64\bin\cmake.exe",
    "C:\msys64\clang64\bin\cmake.exe",
    "C:\Program Files\CMake\bin\cmake.exe"
)

$ninjaCandidates = @(
    "C:\msys64\ucrt64\bin\ninja.exe",
    "C:\msys64\mingw64\bin\ninja.exe",
    "C:\msys64\clang64\bin\ninja.exe"
)

function Find-Tool([string[]]$Candidates, [string]$CommandName) {
    foreach ($candidate in $Candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    $cmd = Get-Command $CommandName -ErrorAction SilentlyContinue
    if ($cmd) {
        return $cmd.Source
    }

    return $null
}

$cc = Find-Tool $gccCandidates "gcc"
$cmake = Find-Tool $cmakeCandidates "cmake"
$ninja = Find-Tool $ninjaCandidates "ninja"

if ($null -eq $cc) {
    throw "MinGW-w64 GCC/Clang not found. Expected MSYS2 UCRT64/MINGW64/CLANG64 or gcc on PATH."
}
if ($null -eq $cmake) {
    throw "CMake not found. Expected e.g. C:\msys64\ucrt64\bin\cmake.exe or cmake on PATH."
}
if ($null -eq $ninja) {
    throw "Ninja not found. Expected e.g. C:\msys64\ucrt64\bin\ninja.exe or ninja on PATH."
}

Write-Host "C compiler: $cc"
Write-Host "CMake:      $cmake"
Write-Host "Ninja:      $ninja"

# IMPORTANT:
# GCC found by absolute path is not enough. GCC launches child tools such as
# cc1.exe/as.exe/ld.exe, and the MinGW runtime DLLs/tools must also be visible
# to those child processes. Add the selected toolchain bin directory (and the
# MSYS2 usr/bin utility directory) to PATH for the whole configure/build step.
$toolBin = Split-Path -Parent $cc
$msysUsrBin = "C:\msys64\usr\bin"
$oldPath = $env:Path
$pathParts = @($toolBin)
if (Test-Path $msysUsrBin) { $pathParts += $msysUsrBin }
$pathParts += $oldPath
$env:Path = ($pathParts -join ";")

Write-Host "Build PATH prefix:"
Write-Host "  $toolBin"
if (Test-Path $msysUsrBin) { Write-Host "  $msysUsrBin" }

# Fail early with a useful diagnostic before CMake's generic
# "compiler cannot compile a simple test program" message.
Write-Host ""
Write-Host "Checking native compiler..."
& $cc --version
if ($LASTEXITCODE -ne 0) {
    throw "gcc/clang itself failed to start (exit $LASTEXITCODE). Check the MSYS2 UCRT64 installation."
}

$probeDir = Join-Path $root "_compiler_probe"
Remove-Item -Recurse -Force $probeDir -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $probeDir | Out-Null
$probeC = Join-Path $probeDir "probe.c"
$probeExe = Join-Path $probeDir "probe.exe"
' int main(void) { return 0; } ' | Set-Content -Encoding ASCII $probeC

& $cc $probeC -o $probeExe
if ($LASTEXITCODE -ne 0 -or !(Test-Path $probeExe)) {
    Write-Host ""
    Write-Host "Toolchain diagnostics:"
    foreach ($name in @("cc1.exe", "as.exe", "ld.exe")) {
        $found = Get-Command $name -ErrorAction SilentlyContinue
        if ($found) {
            Write-Host "  $name -> $($found.Source)"
        } else {
            Write-Host "  $name -> NOT FOUND"
        }
    }
    throw "MinGW-w64 compiler probe failed (exit $LASTEXITCODE). The toolchain install is incomplete or its child tools/runtime are not reachable."
}
Write-Host "Compiler probe OK: $probeExe"

Remove-Item -Recurse -Force $build -ErrorAction SilentlyContinue
& $cmake -S $root -B $build -G Ninja "-DCMAKE_MAKE_PROGRAM=$ninja" "-DCMAKE_BUILD_TYPE=$Configuration" "-DCMAKE_C_COMPILER=$cc"
if ($LASTEXITCODE -ne 0) {
    throw "CMake configure failed (exit $LASTEXITCODE)."
}

& $cmake --build $build --config $Configuration
if ($LASTEXITCODE -ne 0) {
    throw "Native build failed (exit $LASTEXITCODE)."
}

$dll = Get-ChildItem -Path $build -Recurse -Filter "adoom_native.dll" | Select-Object -First 1
if (!$dll) { throw "adoom_native.dll was not produced." }

Copy-Item $dll.FullName (Join-Path $root "adoom_native.dll") -Force
Write-Host "Built native bridge: $(Join-Path $root 'adoom_native.dll')"
