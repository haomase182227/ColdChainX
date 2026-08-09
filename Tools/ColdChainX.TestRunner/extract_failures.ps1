$path = Join-Path $PSScriptRoot 'UploadedSpecs\TestResults_Latest.json'
$raw = Get-Content $path -Raw -Encoding UTF8
$data = $raw | ConvertFrom-Json
$failed = $data.FailedTests
Write-Host "=== TOTAL: $($data.Summary.Total) | PASSED: $($data.Summary.Passed) | FAILED: $($failed.Count) ==="
foreach ($f in $failed) {
    $reason = $f.FailureReason
    if ($reason.Length -gt 300) { $reason = $reason.Substring(0,300) + "..." }
    Write-Host "$($f.FunctionCode)|$($f.TestCaseId)|$($f.Type)|$($f.HttpStatusCode)|$reason"
}
