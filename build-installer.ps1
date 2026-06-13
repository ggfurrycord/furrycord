# build-installer.ps1 - Compile Furrycord-Installer.exe
# Usage: .\build-installer.ps1

$ErrorActionPreference = "Stop"
$Root   = $PSScriptRoot
$SrcDir = Join-Path $Root "installer-src"
$OutDir = Join-Path $Root "release\installer"
$OutExe = Join-Path $OutDir "Furrycord-Installer.exe"

Write-Host ""
Write-Host "  [Furrycord] Compiling installer..." -ForegroundColor Cyan

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# Method 1: dotnet SDK
$hasDotnet = $null
try { $hasDotnet = & dotnet --version 2>$null } catch { }

if ($hasDotnet) {
    Write-Host "  [1/1] dotnet build (SDK $hasDotnet)..." -ForegroundColor DarkGray
    & dotnet publish "$SrcDir\FurrycordInstaller.csproj" `
        -c Release `
        -o $OutDir `
        --nologo `
        -v quiet `
        -p:PublishSingleFile=true `
        -p:SelfContained=false `
        -r win-x64
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  [ERROR] dotnet build failed." -ForegroundColor Red
        exit 1
    }
    # dotnet may place the .exe in a subdirectory
    $built = Get-ChildItem $OutDir -Recurse -Filter "Furrycord-Installer.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($built -and $built.FullName -ne $OutExe) {
        Copy-Item $built.FullName $OutExe -Force
    }
}
# Method 2: csc.exe (.NET Framework - always present on Windows)
else {
    Write-Host "  dotnet SDK not found - using csc.exe (.NET Framework)..." -ForegroundColor Yellow

    $fxDir = "${env:SystemRoot}\Microsoft.NET\Framework64"
    $csc = Get-ChildItem "$fxDir\v4*\csc.exe" -ErrorAction SilentlyContinue |
           Sort-Object FullName -Descending |
           Select-Object -First 1 -ExpandProperty FullName

    if (-not $csc) {
        $fxDir32 = "${env:SystemRoot}\Microsoft.NET\Framework"
        $csc = Get-ChildItem "$fxDir32\v4*\csc.exe" -ErrorAction SilentlyContinue |
               Sort-Object FullName -Descending |
               Select-Object -First 1 -ExpandProperty FullName
    }

    if (-not $csc) {
        Write-Host "  [ERROR] Neither dotnet SDK nor csc.exe found." -ForegroundColor Red
        Write-Host "  -> Install .NET SDK: https://dotnet.microsoft.com/download" -ForegroundColor Yellow
        exit 1
    }

    Write-Host "  [1/1] Compiling with $csc..." -ForegroundColor DarkGray

    $ico    = Join-Path $Root "furrycord.ico"
    $icoArg = if (Test-Path $ico) { "/win32icon:`"$ico`"" } else { "" }

    $refs = @(
        "System.Net.Http.dll",
        "System.IO.Compression.dll",
        "System.IO.Compression.FileSystem.dll",
        "System.Windows.Forms.dll",
        "System.Drawing.dll",
        "System.dll"
    ) | ForEach-Object { "/r:$_" }

    $args = @(
        "/target:winexe",
        "/platform:anycpu",
        "/optimize+",
        "/nologo",
        "/out:`"$OutExe`"",
        "/utf8output"
    ) + $refs

    if ($icoArg) { $args += $icoArg }
    $args += "`"$SrcDir\Program.cs`""

    & $csc @args
    if ($LASTEXITCODE -ne 0) {
        Write-Host "  [ERROR] Compilation failed." -ForegroundColor Red
        exit 1
    }
}

# Result
if (Test-Path $OutExe) {
    $size = [math]::Round((Get-Item $OutExe).Length / 1KB, 0)
    Write-Host ""
    Write-Host "  OK  Furrycord-Installer.exe compiled ($size KB)" -ForegroundColor Green
    Write-Host "    -> $OutExe" -ForegroundColor DarkGray

    # Bundle built mod files so installer works offline (no GitHub download)
    $distSrc = Join-Path $Root "dist\desktop"
    $distDst = Join-Path $OutDir "dist"
    if (Test-Path (Join-Path $distSrc "patcher.js")) {
        Write-Host "  Bundling dist files for offline install..." -ForegroundColor DarkGray
        if (Test-Path $distDst) { Remove-Item $distDst -Recurse -Force }
        Copy-Item $distSrc $distDst -Recurse -Force
        $ico = Join-Path $Root "furrycord.ico"
        if (Test-Path $ico) { Copy-Item $ico (Join-Path $distDst "furrycord.ico") -Force }
        Write-Host "  OK  dist bundled next to installer" -ForegroundColor Green
    } else {
        Write-Host "  WARN  dist\desktop\patcher.js not found — run: pnpm run buildStandalone" -ForegroundColor Yellow
    }
    Write-Host ""
} else {
    Write-Host "  [ERROR] Furrycord-Installer.exe not found after compilation." -ForegroundColor Red
    exit 1
}
