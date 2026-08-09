$path = Join-Path $PSScriptRoot 'UploadedSpecs\TestResults_Latest.json'
$json = Get-Content $path -Raw | ConvertFrom-Json
$failed = $json.FailedTests
Write-Host "Total failed: $($failed.Count)"
Write-Host ""
Write-Host "=== By FunctionCode ==="
$grouped = $failed | Group-Object FunctionCode | Sort-Object Count -Descending
foreach ($g in $grouped) { Write-Host "$($g.Name): $($g.Count) failures" }
Write-Host ""
Write-Host "=== By Type (N=Normal, A=Abnormal, B=Boundary) ==="
$failed | Group-Object Type | ForEach-Object { Write-Host "$($_.Name): $($_.Count)" }
Write-Host ""
Write-Host "=== Failure Pattern Summary ==="
$missingValidation = ($failed | Where-Object { $_.FailureReason -match 'Expected error.*but got (200|201)' }).Count
$unexpectedError = ($failed | Where-Object { $_.FailureReason -match 'Expected 2xx success but got HTTP [45]' }).Count
$other = ($failed | Where-Object { $_.FailureReason -notmatch 'Expected error.*but got (200|201)' -and $_.FailureReason -notmatch 'Expected 2xx success but got HTTP [45]' }).Count
Write-Host "MISSING_VALIDATION (Backend returned 200 but should have returned error): $missingValidation"
Write-Host "UNEXPECTED_ERROR (Test expected success but got 4xx error): $unexpectedError"
Write-Host "OTHER: $other"
