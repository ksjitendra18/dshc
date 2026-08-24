# Build the CKYC Processor solution.
# Notes:
#  * -m:1 / -nodeReuse:false avoid MSBuild's parallel node spawning, which a restricted
#    sandbox can block — safe to keep everywhere.
#  * -p:NuGetAudit=false avoids an outbound vulnerability-audit network call on offline boxes.
Set-Location $PSScriptRoot
# Keep the target framework in one place so the build/run scripts never drift from it.
$tfm = 'net10.0'
dotnet build 'src\CKYC.Processor\CKYC.Processor.csproj' -c Release -p:NuGetAudit=false -m:1 -nodeReuse:false
Write-Host "`nBuilt: $PSScriptRoot\src\CKYC.Processor\bin\Release\$tfm\CKYC.Processor.exe"
