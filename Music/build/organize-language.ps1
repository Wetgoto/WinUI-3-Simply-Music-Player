param(
    [Parameter(Mandatory = $true)]
    [string]$OutputDirectory,

    [string[]]$KeepAtRoot = @('zh-CN', 'en-us')
)

$ErrorActionPreference = 'Stop'

$outputPath = [System.IO.Path]::GetFullPath($OutputDirectory)
if (-not (Test-Path -LiteralPath $outputPath)) {
    throw "Output directory not found: $outputPath"
}

$languageRoot = Join-Path $outputPath 'language'
$moved = 0

# WinUI resolves its .mui files relative to the executable. Keep the current
# UI language and the English fallback at the root; group all other cultures.
foreach ($directory in Get-ChildItem -LiteralPath $outputPath -Directory) {
    if ($directory.Name -eq 'language' -or $KeepAtRoot -contains $directory.Name) {
        continue
    }

    $muiFiles = Get-ChildItem -LiteralPath $directory.FullName -File -Filter '*.mui' -ErrorAction SilentlyContinue
    if (-not $muiFiles) {
        continue
    }

    if (-not (Test-Path -LiteralPath $languageRoot)) {
        New-Item -ItemType Directory -Path $languageRoot | Out-Null
    }

    $destination = Join-Path $languageRoot $directory.Name
    if (Test-Path -LiteralPath $destination) {
        Get-ChildItem -LiteralPath $directory.FullName -Force |
            Move-Item -Destination $destination -Force
        Remove-Item -LiteralPath $directory.FullName -Recurse -Force
    }
    else {
        Move-Item -LiteralPath $directory.FullName -Destination $destination
    }

    $moved++
}

if ($moved -gt 0) {
    Write-Host "Moved $moved language folder(s) into $languageRoot"
}
