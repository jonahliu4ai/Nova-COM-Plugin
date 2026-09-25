param(
    [string]$Root = "D:\src\nova-com-plugin\ComComMiddleware"
)

Set-Location $Root

$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = Join-Path $env:WINDIR "Microsoft.NET\Framework\v4.0.30319\csc.exe"
}
if (-not (Test-Path $csc)) {
    throw "没找到 csc.exe"
}

$coreSrc  = Join-Path $Root "ComComMiddleware.Core.cs"
$uiSrc    = Join-Path $Root "ComComMiddleware.UI.cs"
$asmInfo  = Join-Path $Root "AssemblyInfo.cs"
$logo     = Join-Path $Root "logo.ico"
$smoke    = Join-Path $Root "SmokeTests.cs"
$e2e      = Join-Path $Root "TestSevenStarE2E.cs"
$outCore  = Join-Path $Root "ComComMiddleware.Core.dll"
$outApp   = Join-Path $Root "ComComMiddleware.exe"
$outSmoke = Join-Path $Root "SmokeTests.exe"
$outE2e   = Join-Path $Root "TestSevenStarE2E.exe"

# 1) Core 类库（无 UI 依赖）
& $csc /nologo /target:library /out:$outCore `
    /r:System.Core.dll /r:System.Xml.dll `
    $coreSrc
if ($LASTEXITCODE -ne 0) { throw "Core 编译失败，退出码=$LASTEXITCODE" }

# 2) WinForms UI
$win32icon = ""
if (Test-Path $logo) {
    $win32icon = "/win32icon:""$logo"""
}
& $csc /nologo /target:winexe /out:$outApp `
    /r:$outCore /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Xml.dll `
    $win32icon `
    $uiSrc $asmInfo
if ($LASTEXITCODE -ne 0) { throw "UI 编译失败，退出码=$LASTEXITCODE" }

# 3) 烟雾测试（可选但建议每次都跑）
& $csc /nologo /target:exe /out:$outSmoke /r:System.Core.dll /r:$outCore $smoke
if ($LASTEXITCODE -ne 0) { throw "SmokeTests 编译失败，退出码=$LASTEXITCODE" }

# 4) SevenStar 端到端联调（COM0COM 虚拟串口对 COM7<->COM8，仅编译；需先装 com0com）
& $csc /nologo /target:exe /out:$outE2e /r:System.Core.dll /r:$outCore $e2e
if ($LASTEXITCODE -ne 0) { throw "TestSevenStarE2E 编译失败，退出码=$LASTEXITCODE" }

Write-Host "Build OK"