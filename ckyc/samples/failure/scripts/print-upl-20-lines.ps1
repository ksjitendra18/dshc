# Prints the "line number" of every record type 20 in a generated .UPL batch file.
# These are the numbers a CERSAI reply (record 100, field[2] "input record-20
# line no") must use to attribute the reply back to the right customer,
# because build-zip stored exactly this value on each master row
# (master_record.BatchRecordLine).
#
# Usage (after `build-zip`):
#   .\samples\failure\scripts\print-upl-20-lines.ps1 `
#     -Path .\runtime\output\I_IAU010441_IN0238_24082026_00003\upload\I_IAU010441_IN0238_24082026_00003.UPL
#
# Sample output:
#   file line 2   record20 line 1   name Ashish Kumar
#   file line 8   record20 line 7   name Priya Sharma
# Use the "record20 line" column when filling <R20LINE> in the .RES template.
param(
    [Parameter(Mandatory = $true)]
    [string]$Path
)

if (-not (Test-Path $Path)) { Write-Error "File not found: $Path"; exit 1 }

$fileLine = 0
Get-Content $Path | ForEach-Object {
    $fileLine++
    $f = $_ -split '\|'
    if ($f[0] -eq '20') {
        "{0,-8} record20 line {1,-4} name {2} {3}" -f ("(file line {0})" -f $fileLine), $f[1], $f[5], $f[7]
    }
}