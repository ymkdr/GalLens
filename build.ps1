# 重新编译并发布 GalLens.exe
# 用法：在 D:\GAL\GalLens 下执行  .\build.ps1
#
# 两个坑：
#   1) 不能用 -o .（直接发布到项目根目录），那样生成的 exe 会损坏，
#      现象是程序能启动但加载 XAML 抛 NotImplementedException，主窗口建不出来。
#   2) 也不要发布到项目里的 dist\，会产生第二个 exe 让人分不清。
# 所以这里发布到系统临时目录，复制完就删掉，项目里永远只有一个 GalLens.exe。

param([string]$Configuration = "Release")

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$stage = Join-Path $env:TEMP "GalLens-publish"

Push-Location $root
try {
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue

    Write-Host "编译并发布 ..." -ForegroundColor Cyan
    dotnet publish "$root\src\GalLens\GalLens.csproj" -c $Configuration -r win-x64 --self-contained false -p:PublishSingleFile=true -p:DebugType=none -o $stage

    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败" }

    Copy-Item "$stage\GalLens.exe" "$root\GalLens.exe" -Force
    Remove-Item $stage -Recurse -Force -ErrorAction SilentlyContinue

    $size = [math]::Round((Get-Item "$root\GalLens.exe").Length / 1MB, 2)
    Write-Host "完成 -> $root\GalLens.exe  ($size MB)" -ForegroundColor Green
}
finally {
    Pop-Location
}
