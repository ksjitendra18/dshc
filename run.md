Remove-Item .\runtime\ckyc.db -ErrorAction SilentlyContinue # clean slate
.\build.ps1
$exe = ".\src\CKYC.Processor\bin\Release\net10.0\CKYC.Processor.exe"
& $exe insert --file .\samples\retail-customer.json
& $exe build-zip
& $exe fvu
& $exe status

dotnet run --project src/CKYC.Processor -- `  documents import`
--customer-id CUST-RETAIL-SKS `
--dir staging/CUST-RETAIL-SKS
