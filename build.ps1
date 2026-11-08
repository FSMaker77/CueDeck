$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$fw = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$csc = Join-Path $fw 'csc.exe'
if (-not (Test-Path $csc)) { throw "csc.exe non trovato: $csc" }

$assets = Join-Path $root 'assets'
$dist = Join-Path $root 'dist'
$tools = Join-Path $root 'tools'
New-Item -ItemType Directory -Force -Path $assets, $dist | Out-Null

$icon = Join-Path $assets 'cuedeck.ico'
$iconSource = Join-Path $tools 'MakeIcon.cs'
$iconExe = Join-Path $tools 'MakeIcon.exe'
if (-not (Test-Path $icon) -or (Get-Item $iconSource).LastWriteTime -gt (Get-Item $icon).LastWriteTime) {
    & $csc /nologo /codepage:65001 /out:$iconExe $iconSource
    if ($LASTEXITCODE -ne 0) { throw 'Compilazione icona non riuscita.' }
    & $iconExe $icon
    if ($LASTEXITCODE -ne 0) { throw 'Generazione icona non riuscita.' }
}

$src = Join-Path $root 'src'
$files = @(Get-ChildItem -Path $src -Filter '*.cs' | ForEach-Object { $_.FullName })
$exe = Join-Path $dist 'CueDeck.exe'
$manifest = Join-Path $src 'app.manifest'
$webExtensions = Join-Path $fw 'System.Web.Extensions.dll'
& $csc /nologo /codepage:65001 /target:winexe /platform:anycpu /optimize+ /langversion:5 `
    /win32icon:$icon /win32manifest:$manifest `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll /reference:$webExtensions `
    /out:$exe $files
if ($LASTEXITCODE -ne 0) { throw 'Compilazione CueDeck non riuscita.' }

$distDocs = Join-Path $dist 'docs'
if (Test-Path $distDocs) { Remove-Item -Recurse -Force $distDocs }
Copy-Item -Path (Join-Path $root 'docs') -Destination $dist -Recurse -Force
Copy-Item -Path (Join-Path $root 'README.md') -Destination $dist -Force
Copy-Item -Path (Join-Path $root 'README.en.md') -Destination $dist -Force
Copy-Item -Path (Join-Path $root 'LICENSE') -Destination $dist -Force
Copy-Item -Path (Join-Path $root 'CHANGELOG.md') -Destination $dist -Force
$zip = Join-Path $dist 'CueDeck-1.1.zip'
Get-ChildItem -Path $dist -Filter 'CueDeck-*.zip' -ErrorAction SilentlyContinue | Remove-Item -Force
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path $exe, (Join-Path $root 'README.md'), (Join-Path $root 'README.en.md'), (Join-Path $root 'LICENSE'), (Join-Path $root 'CHANGELOG.md'), $distDocs -DestinationPath $zip -Force
$hashExe = (Get-FileHash $exe -Algorithm SHA256).Hash
$hashZip = (Get-FileHash $zip -Algorithm SHA256).Hash
$checksums = @(
    ($hashExe + '  CueDeck.exe'),
    ($hashZip + '  CueDeck-1.1.zip')
)
$checksums | Set-Content -LiteralPath (Join-Path $dist 'SHA256SUMS.txt') -Encoding ASCII

$info = Get-Item $exe
Write-Host ("CueDeck.exe: {0:N0} KB" -f ($info.Length / 1KB))
Write-Host ("Output: {0}" -f $exe)