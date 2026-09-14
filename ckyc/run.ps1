# Run the full centralized CKYC pipeline end-to-end:
#   fetch cust -> crm serve (background) -> store -> retry -> search-customer -> build-zip -> fvu -> status
# The FVU step spawns FVU_RUN_UTILITY.exe (a PyInstaller bundle) which unpacks its runtime to
# the system temp; on a machine where a sandbox blocks that, run this script elevated or run
# the `fvu` step outside the sandbox.
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

$tfm = 'net10.0'
$exe = Join-Path $PSScriptRoot "src\CKYC.Processor\bin\Release\$tfm\CKYC.Processor.exe"
if (-not (Test-Path $exe)) { Write-Host "Building first..."; & .\build.ps1 }

$crmUrl = 'http://127.0.0.1:5291'

Write-Host "=== 1/7 fetch cust ==="; & $exe fetch cust

Write-Host "=== 2/7 starting CRM API ($crmUrl) ==="
$crm = Start-Process -FilePath $exe -ArgumentList @('crm','serve','--urls',$crmUrl) -PassThru -NoNewWindow
try {
    for ($i = 0; $i -lt 30; $i++) {
        try { if ((Invoke-WebRequest "$crmUrl/health" -UseBasicParsing -TimeoutSec 2).StatusCode -eq 200) { break } } catch { Start-Sleep -Milliseconds 500 }
        Start-Sleep -Milliseconds 500
    }

    Write-Host "=== 3/7 store ==="; & $exe store
    Write-Host "=== 4/7 retry (recovers simulated save failures) ==="; & $exe retry
    Write-Host "=== 5/7 search-customer (pre-batch CKYCR search) ==="; & $exe search-customer
    Write-Host "=== 6/7 build-zip ==="; & $exe build-zip
    Write-Host "=== 7/7 fvu (real FVU_RUN_UTILITY.exe) ==="; & $exe fvu

    Write-Host "=== status ==="; & $exe status
}
finally {
    Stop-Process -Id $crm.Id -Force -ErrorAction SilentlyContinue
    Write-Host "CRM API stopped."
}
