# sanitize-docs-secrets.ps1 - One-shot secret sanitizer for docs/ (2026-07-18)
# Replaces leaked credential literals with {{PLACEHOLDER}} tokens in tracked docs.
# Pure-ASCII script; UTF-8 read/write without BOM.

$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$docs = Join-Path $repo "docs"
$utf8NoBom = New-Object System.Text.UTF8Encoding($false)

$map = [ordered]@{
    "32X+RIXP5/Vh"                          = "{{SSH_PASSWORD}}"
    "qazwsxedcrfvtgbyhnujmikolpqazwsx"      = "{{JWT_KEY}}"
    "Agent1-Production-JWT-Key-2024-Secure" = "{{JWT_KEY}}"
    "dlyayiibtlwldefb"                      = "{{SMTP_AUTH_CODE}}"
    "postgres123"                           = "{{DB_PASSWORD}}"
    "7758521"                               = "{{DB_PASSWORD}}"
}

$changed = New-Object System.Collections.Generic.List[string]
Get-ChildItem -LiteralPath $docs -Recurse -File -Include *.md, *.html | ForEach-Object {
    $text = [System.IO.File]::ReadAllText($_.FullName)
    $orig = $text
    foreach ($k in $map.Keys) { $text = $text.Replace($k, $map[$k]) }
    if ($text -ne $orig) {
        [System.IO.File]::WriteAllText($_.FullName, $text, $utf8NoBom)
        $changed.Add($_.FullName.Substring($repo.Length + 1))
    }
}

Write-Output ("SANITIZED_COUNT=" + $changed.Count)
$changed | ForEach-Object { Write-Output ("  - " + $_) }
$report = Join-Path $PSScriptRoot "sanitized-docs-list.txt"
$changed | Out-File -FilePath $report -Encoding UTF8
