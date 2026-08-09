git reset ab55b80

$files = git status --porcelain
$count = 0
foreach ($line in $files) {
    if ([string]::IsNullOrWhiteSpace($line)) { continue }
    $action = $line.Substring(0, 2).Trim()
    $file = $line.Substring(3).Trim()
    
    if ($file.StartsWith('"') -and $file.EndsWith('"')) {
        $file = $file.Substring(1, $file.Length - 2)
    }

    $filename = Split-Path $file -Leaf

    if ($action -eq "D") {
        git rm $file
        git commit -m "Delete $filename"
    } else {
        git add $file
        if ($action -eq "??" -or $action -eq "A") {
            git commit -m "Add $filename"
        } else {
            git commit -m "Update $filename"
        }
    }
    $count++
}

Write-Output "Successfully committed $count files individually."
