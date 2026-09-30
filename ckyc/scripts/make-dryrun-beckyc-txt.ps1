<#
.SYNOPSIS
    Creates the beckyc-style base64 payload file used by the offline dry run.

.DESCRIPTION
    The beckyc channel publishes a customer's image as a single
    `data:<mime>;base64,<payload>` line inside a `.txt` file, in a folder named by the
    document key (dockey):

        <downloadRoot>\inbox\<channel>\<dockey>\<file>.txt

    This script writes that file into the simulated local inbox so `documents fetch`
    (with documentFetch.channels.beckyc.useRealSftp = false) can decode and import it.

    With no -Source it writes a valid 1x1 JPEG placeholder, which is enough to prove the
    fetch/decode/slot/overlay path. Pass -Source <image> to wrap a real JPEG/PNG/PDF instead,
    or -Raw <image> to drop the raw file into the inbox (then use a matching pattern).

.EXAMPLE
    # placeholder for GTR007375 / dockey 0871808577544330
    .\scripts\make-dryrun-beckyc-txt.ps1

.EXAMPLE
    # wrap the real customer photo (JPEG/PNG/PDF are detected)
    .\scripts\make-dryrun-beckyc-txt.ps1 -Source .\myphoto.jpg
#>
[CmdletBinding()]
param(
    [string] $CustomerId = 'GTR007375',
    [string] $Dockey     = '0871808577544330',
    [string] $Channel    = 'beckyc',
    [string] $DownloadRoot,
    [string] $FileName,
    [string] $Source,
    [string] $Raw
)

$ErrorActionPreference = 'Stop'

# Resolve the repo root whether the script is run directly or from another folder.
$scriptDir = $PSScriptRoot
if (-not $scriptDir) { $scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path }
if (-not $DownloadRoot) { $DownloadRoot = Join-Path (Split-Path -Parent $scriptDir) 'runtime\document-fetch' }

# A valid 1x1 JPEG (enough to satisfy the content sniff + document store signature check).
$PlaceholderJpegBase64 =
    '/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAgGBgcGBQgHBwcJCQgKDBQNDAsLDBkSEw8UHRofHh0aHBwgJC4nICIsIxwcKDcpLDAxNDQ0Hyc5PTgyPC4zNDL/2wBDAQkJCQwLDBgNDRgyIRwhMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjIyMjL/wAARCAABAAEDASIAAhEBAxEB/8QAHwAAAQUBAQEBAQEAAAAAAAAAAAECAwQFBgcICQoL/8QAtRAAAgEDAwIEAwUFBAQAAAF9AQIDAAQRBRIhMUEGE1FhByJxFDKBkaEII0KxwRVS0fAkM2JyggkKFhcYGRolJicoKSo0NTY3ODk6Q0RFRkdISUpTVFVWV1hZWmNkZWZnaGlqc3R1dnd4eXqDhIWGh4iJipKTlJWWl5iZmqKjpKWmp6ipqrKztLW2t7i5usLDxMXGx8jJytLT1NXW19jZ2uHi4+Tl5ufo6erx8vP09fb3+Pn6/8QAHwEAAwEBAQEBAQEBAQAAAAAAAAECAwQFBgcICQoL/8QAtREAAgECBAQDBAcFBAQAAQJ3AAECAxEEBSExBhJBUQdhcRMiMoEIFEKRobHBCSMzUvAVYnLRChYkNOEl8RcYGRomJygpKjU2Nzg5OkNERUZHSElKU1RVVldYWVpjZGVmZ2hpanN0dXZ3eHl6goOEhYaHiImKkpOUlZaXmJmaoqOkpaanqKmqsrO0tba3uLm6wsPExcbHyMnK0tPU1dbX2Nna4uPk5ebn6Onq8vP09fb3+Pn6/9oADAMBAAIRAxEAPwD3+iiigD//2Q=='

function Get-Mime([byte[]] $bytes) {
    if ($bytes.Length -ge 3 -and $bytes[0] -eq 0xFF -and $bytes[1] -eq 0xD8 -and $bytes[2] -eq 0xFF) { return 'image/jpeg' }
    if ($bytes.Length -ge 8 -and $bytes[0] -eq 0x89 -and $bytes[1] -eq 0x50 -and $bytes[2] -eq 0x4E -and $bytes[3] -eq 0x47) { return 'image/png' }
    if ($bytes.Length -ge 5 -and [System.Text.Encoding]::ASCII.GetString($bytes, 0, 5) -eq '%PDF-') { return 'application/pdf' }
    throw 'Unsupported content (only JPEG, PNG and PDF are accepted).'
}

$inbox = Join-Path (Join-Path (Join-Path $DownloadRoot 'inbox') $Channel) $Dockey
New-Item -ItemType Directory -Force -Path $inbox | Out-Null

if ($FileName) { $name = $FileName } else { $name = "$Dockey.txt" }

if ($Raw) {
    $target = Join-Path $inbox ([IO.Path]::GetFileName($Raw))
    Copy-Item -LiteralPath $Raw -Destination $target -Force
    Write-Host "[dryrun] Raw file copied  : $target"
    Write-Host "[dryrun] Dockey           : $Dockey"
    return
}

if ($Source) {
    $bytes   = [IO.File]::ReadAllBytes((Resolve-Path -LiteralPath $Source).Path)
    $mime    = Get-Mime $bytes
    $payload = "data:$mime;base64," + [Convert]::ToBase64String($bytes)
    Write-Host "[dryrun] Wrapping $Source ($mime)"
}
else {
    $payload = "data:image/jpeg;base64,$PlaceholderJpegBase64"
    Write-Host "[dryrun] Writing 1x1 JPEG placeholder"
}

$path = Join-Path $inbox $name
Set-Content -LiteralPath $path -Value $payload -Encoding ASCII -NoNewline
Write-Host "[dryrun] Beckyc payload   : $path"
Write-Host "[dryrun] Customer id      : $CustomerId"
Write-Host "[dryrun] Dockey           : $Dockey"
