param(
    [Parameter(Mandatory=$true)]
    [string]$ManagedPath,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$proj = Join-Path $root "ADOOMFAIAccelerator\ADOOMFAIAccelerator.csproj"
$out = Join-Path $root "dist\ADOOMFAIAccelerator"
$native = Join-Path $root "native\adoom_native.dll"

Remove-Item -Recurse -Force (Join-Path $root "dist") -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $out | Out-Null

& (Join-Path $root "native\build-native.ps1") -Configuration $Configuration

dotnet build $proj -c $Configuration /p:GameManagedPath="$ManagedPath"
Copy-Item (Join-Path $root "ADOOMFAIAccelerator\bin\$Configuration\net481\ADOOMFAIAccelerator.dll") $out
Copy-Item $native $out
New-Item -ItemType Directory -Force (Join-Path $out "iwad") | Out-Null
Copy-Item (Join-Path $root "release-template\Info.json") $out
Copy-Item (Join-Path $root "release-template\JAModInfo.json") $out

$bootstrap = Join-Path $ManagedPath "..\..\Mods\JALib\JAMod.Bootstrap.dll"
if (!(Test-Path $bootstrap)) {
    throw "JAMod.Bootstrap.dll not found at $bootstrap"
}
Copy-Item $bootstrap $out

$zip = Join-Path $root "dist\ADOOMFAIAccelerator-v0.5.4.zip"
Compress-Archive -Path "$out\*" -DestinationPath $zip -Force
Write-Host "Built: $zip"
