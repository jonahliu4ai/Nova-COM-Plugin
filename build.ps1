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

$src      = Join-Path $Root "ComComMiddleware.cs"
$smoke    = Join-Path $Root "SmokeTests.cs"
$outApp   = Join-Path $Root "ComComMiddleware.exe"
$outSmoke = Join-Path $Root "SmokeTests.exe"

# 1) 主程序
& $csc /nologo /target:winexe /out:$outApp `
    /r:System.Windows.Forms.dll /r:System.Drawing.dll /r:System.Core.dll /r:System.Xml.dll `
    $src
if ($LASTEXITCODE -ne 0) { throw "主程序编译失败，退出码=$LASTEXITCODE" }

# 2) 烟雾测试（可选但建议每次都跑）
& $csc /nologo /target:exe /out:$outSmoke /r:System.Core.dll /r:$outApp $smoke
if ($LASTEXITCODE -ne 0) { throw "SmokeTests 编译失败，退出码=$LASTEXITCODE" }

Write-Host "Build OK"
