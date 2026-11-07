$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
& "$root\build.bat"
if ($LASTEXITCODE -ne 0) { throw "Build failed" }

$p1 = Start-Process -FilePath "$root\IpSwitcher.exe" -ArgumentList "--self-test" -Wait -PassThru -NoNewWindow
if ($p1.ExitCode -ne 0) { throw "Self-test failed with exit code $($p1.ExitCode)" }

$p2 = Start-Process -FilePath "$root\IpSwitcher.exe" -ArgumentList "--smoke-ui" -Wait -PassThru -NoNewWindow
if ($p2.ExitCode -ne 0) { throw "UI smoke test failed with exit code $($p2.ExitCode)" }

Write-Host "ALL TESTS PASSED"
