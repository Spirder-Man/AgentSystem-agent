# 解析最新 trx，按测试类分组统计失败用例（区分 PG 环境依赖与真实回归）
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$root = Split-Path $PSScriptRoot -Parent
$trx = Get-ChildItem -Path (Join-Path $root 'Agent1.Tests\TestResults') -Filter '*.trx' |
    Sort-Object LastWriteTime -Descending | Select-Object -First 1
Write-Output ("TRX: " + $trx.Name)

[xml]$doc = Get-Content $trx.FullName -Encoding UTF8
$results = $doc.TestRun.Results.UnitTestResult
$failed = $results | Where-Object { $_.outcome -eq 'Failed' }
Write-Output ("Total=" + $results.Count + " Failed=" + $failed.Count)

# 失败用例按类名分组
$failed | ForEach-Object {
    $n = $_.testName
    if ($n -match '^([\w\.]+)\.[^\.]+(\(.*\))?$') { $Matches[1] } else { $n }
} | Group-Object | Sort-Object Count -Descending | ForEach-Object {
    Write-Output ($_.Count.ToString().PadLeft(4) + "  " + $_.Name)
}

# 非 PG 连接错误的失败（真实回归候选）
Write-Output "`n--- Non-PG failures (first 20) ---"
$failed | Where-Object {
    $msg = $_.Output.ErrorInfo.Message
    $msg -notmatch 'PostgreSQL|Npgsql|localhost:5432|127\.0\.0\.1:5432'
} | Select-Object -First 20 | ForEach-Object {
    Write-Output ("* " + $_.testName)
    Write-Output ("    " + (($_.Output.ErrorInfo.Message -split "`n")[0]))
}
