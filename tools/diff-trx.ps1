# 对比两份 trx 的失败集合，找出新增失败（真实回归）
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$dir = Join-Path (Split-Path $PSScriptRoot -Parent) 'Agent1.Tests\TestResults'
[xml]$old = Get-Content (Join-Path $dir 'multimodal-fix-full.trx') -Encoding UTF8
[xml]$new = Get-Content (Join-Path $dir 'p1-verify.trx') -Encoding UTF8
$of = $old.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq 'Failed' } | ForEach-Object { $_.testName }
$nf = $new.TestRun.Results.UnitTestResult | Where-Object { $_.outcome -eq 'Failed' } | ForEach-Object { $_.testName }
Write-Output ("old_failed=" + $of.Count + " new_failed=" + $nf.Count)
$added = @($nf | Where-Object { $of -notcontains $_ })
Write-Output ("NEW_FAILURES=" + $added.Count)
$added | ForEach-Object { Write-Output ("+ " + $_) }
$fixed = @($of | Where-Object { $nf -notcontains $_ })
Write-Output ("FIXED=" + $fixed.Count)
$fixed | ForEach-Object { Write-Output ("- " + $_) }
