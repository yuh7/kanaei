#requires -Version 5
# かなえい をビルドする。Windows 同梱の .NET Framework コンパイラだけを使うので追加インストールは不要。
$ErrorActionPreference = 'Stop'

$root    = Split-Path -Parent $MyInvocation.MyCommand.Path
$src     = Join-Path $root 'src'
$tools   = Join-Path $root 'tools'
$assets  = Join-Path $root 'assets'
$out     = Join-Path $root 'bin'
$exe     = Join-Path $out 'Kanaei.exe'
$ico     = Join-Path $assets 'Kanaei.ico'

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path $csc)) { throw "csc.exe が見つかりません: $csc" }

foreach ($d in @($out, $assets)) {
    if (-not (Test-Path $d)) { New-Item -ItemType Directory -Path $d | Out-Null }
}

# --- 1. アイコンを生成する（描画コードはアプリ本体と共有） ---
$makeIcon = Join-Path $out 'MakeIcon.exe'
$iconArgs = @(
    '/nologo', '/target:exe', '/optimize+', '/codepage:65001'
    "/out:$makeIcon"
    '/reference:System.dll', '/reference:System.Drawing.dll'
    (Join-Path $tools 'MakeIcon.cs')
    (Join-Path $src 'Glyphs.cs')
)
& $csc @iconArgs
if ($LASTEXITCODE -ne 0) { throw "アイコン生成ツールのビルドに失敗しました (exit $LASTEXITCODE)" }

& $makeIcon $ico (Join-Path $assets 'preview')
if ($LASTEXITCODE -ne 0) { throw "アイコンの生成に失敗しました (exit $LASTEXITCODE)" }

# --- 2. 本体をビルドする ---
$sources = Get-ChildItem -Path $src -Filter *.cs | ForEach-Object { $_.FullName }

$appArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/codepage:65001'
    '/warn:4'
    "/out:$exe"
    "/win32icon:$ico"
    '/reference:System.dll'
    '/reference:System.Drawing.dll'
    '/reference:System.Windows.Forms.dll'
) + $sources

& $csc @appArgs
if ($LASTEXITCODE -ne 0) { throw "ビルドに失敗しました (exit $LASTEXITCODE)" }

Remove-Item -LiteralPath $makeIcon -Force -ErrorAction SilentlyContinue

Write-Host ""
Write-Host "ビルド完了: $exe" -ForegroundColor Green
