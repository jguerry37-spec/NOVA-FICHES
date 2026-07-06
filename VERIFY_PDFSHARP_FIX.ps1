# Quick verification for the PDFsharp 6.x font-style fix
Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$theme = Join-Path $root "src\NovaFiches.PdfSharpEngine\NovatlasTheme.cs"

if (!(Test-Path $theme)) { throw "Missing file: $theme" }

$content = Get-Content $theme -Raw
if ($content -notmatch "XFontStyleEx\.Regular") { throw "Fix missing: XFontStyleEx.Regular not found in NovatlasTheme.cs" }
if ($content -notmatch "XFontStyleEx\.Bold") { throw "Fix missing: XFontStyleEx.Bold not found in NovatlasTheme.cs" }

Write-Host "OK: NovatlasTheme.cs uses XFontStyleEx (Regular/Bold)."
