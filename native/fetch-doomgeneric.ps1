param()

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$third = Join-Path $root "third_party"
$repo = Join-Path $third "doomgeneric"
$commit = "dcb7a8dbc7a16ce3dda29382ac9aae9d77d21284"

New-Item -ItemType Directory -Force $third | Out-Null

if (!(Test-Path (Join-Path $repo ".git"))) {
    git clone https://github.com/ozkl/doomgeneric.git $repo
}

git -C $repo fetch --all --tags
git -C $repo checkout --detach $commit

Write-Host "doomgeneric pinned at $commit"
