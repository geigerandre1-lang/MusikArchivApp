param(
    [Parameter(Mandatory = $true)]
    [string]$Version
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
Set-Location -LiteralPath $root

$out = Join-Path $root "publish\MusikArchivApp-portable"
$stage = Join-Path $root "publish\portable-stage"
$zip = Join-Path $root "publish\MusikArchivApp-portable-win-x64-v$Version.zip"

if (-not (Test-Path -LiteralPath (Join-Path $out "MusikArchivApp.exe"))) {
    throw "MusikArchivApp.exe fehlt in $out — zuerst dotnet publish."
}

if (Test-Path -LiteralPath $stage) {
    Remove-Item -LiteralPath $stage -Recurse -Force
}
New-Item -ItemType Directory -Path $stage | Out-Null

Get-ChildItem -LiteralPath $out -Force | Where-Object { $_.Name -ne "data" } | ForEach-Object {
    Copy-Item -LiteralPath $_.FullName -Destination (Join-Path $stage $_.Name) -Recurse -Force
}

if (Test-Path -LiteralPath $zip) {
    Remove-Item -LiteralPath $zip -Force
}
Compress-Archive -Path (Join-Path $stage "*") -DestinationPath $zip -Force
Remove-Item -LiteralPath $stage -Recurse -Force

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [System.IO.Compression.ZipFile]::OpenRead($zip)
try {
    $names = $archive.Entries | ForEach-Object { $_.FullName.Replace("\", "/") }
    if ($names | Where-Object { $_ -eq "data" -or $_.StartsWith("data/") }) {
        throw "ZIP enthält data/ — das darf nicht ins Release."
    }
    if (-not ($names | Where-Object { $_ -eq "MusikArchivApp.exe" })) {
        throw "ZIP enthält keine MusikArchivApp.exe im Root."
    }
}
finally {
    $archive.Dispose()
}

Get-Item -LiteralPath $zip | Select-Object FullName, Length
