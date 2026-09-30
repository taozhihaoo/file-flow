# Creates a demo data set for FileFlow under .\demo-data (fictional content only).
# Usage:  powershell -ExecutionPolicy Bypass -File scripts\create-demo-data.ps1 [-TargetDir path]

param(
    [string]$TargetDir = (Join-Path (Get-Location) "demo-data")
)

$ErrorActionPreference = "Stop"

if (Test-Path $TargetDir) {
    Write-Host "Removing existing demo data at $TargetDir"
    Remove-Item $TargetDir -Recurse -Force
}

$folders = @("Documents", "Invoices", "Reports", "Images", "Projects\Alpha", "Projects\Beta", "Archive")
foreach ($folder in $folders) {
    New-Item -ItemType Directory -Path (Join-Path $TargetDir $folder) -Force | Out-Null
}

function New-DemoFile {
    param([string]$RelativePath, [string]$Content)
    $fullPath = Join-Path $TargetDir $RelativePath
    $directory = Split-Path $fullPath -Parent
    if (-not (Test-Path $directory)) {
        New-Item -ItemType Directory -Path $directory -Force | Out-Null
    }
    [System.IO.File]::WriteAllText($fullPath, $Content)
}

$clients = @("Contoso", "Northwind", "Fabrikam", "AdventureWorks", "Tailspin")
$months  = @("01", "02", "03", "04", "05", "06", "07", "08", "09", "10", "11", "12")

# Invoices: invoice_2026_XX_client.txt
$invoiceCount = 0
foreach ($client in $clients) {
    foreach ($month in $months) {
        if (($invoiceCount + $month) -match "13" ) { continue } # keep the set under ~70 files
        New-DemoFile -RelativePath "Invoices\invoice_2026_$($month)_$($client.ToLower()).txt" -Content @"
INVOICE (demo data, fictional)
Client : $client
Period : 2026-$month
Amount : 1.240,00 EUR
Status : paid
"@
        $invoiceCount++
    }
}

# Documents: notes, contracts, meeting minutes
$topics = @("contract", "notes", "meeting", "policy", "checklist", "manual", "letter")
foreach ($topic in $topics) {
    foreach ($n in 1..3) {
        New-DemoFile -RelativePath "Documents\$($topic)_0$n.txt" -Content @"
Demo document: $topic number $n.
This is fictional placeholder content created by scripts/create-demo-data.ps1.
"@
    }
}

# Reports: quarterly reports
foreach ($quarter in 1..4) {
    New-DemoFile -RelativePath "Reports\report_2026_Q$quarter.pdf.txt" -Content @"
Quarterly report Q$quarter / 2026 (demo data, fictional).
Revenue and figures are placeholders.
"@
}
New-DemoFile -RelativePath "Reports\summary_2026.csv" -Content (@(
    "quarter,revenue,expenses",
    "Q1,120000,98000",
    "Q2,135000,101000",
    "Q3,128000,99000",
    "Q4,150000,110000"
) -join [Environment]::NewLine)

# Images: empty placeholder files with image extensions
foreach ($n in 1..8) {
    $ext = if ($n % 2 -eq 0) { "jpg" } else { "png" }
    $path = Join-Path $TargetDir "Images\photo_0$n.$ext"
    # small valid-ish binary placeholder (not a real image, just bytes for scanning)
    [System.IO.File]::WriteAllBytes($path, [byte[]](0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A) * 4)
}

# Projects
foreach ($project in @("Alpha", "Beta")) {
    foreach ($n in 1..4) {
        New-DemoFile -RelativePath "Projects\$project\$($project.ToLower())_spec_v$n.md" -Content @"
# $project specification v$n (demo data, fictional)

Scope, milestones and notes are placeholders for the FileFlow demo data set.
"@
    }
    New-DemoFile -RelativePath "Projects\$project\TODO.txt" -Content "TODO list for $project (fictional)."
}

# Archive: old files
foreach ($n in 1..5) {
    New-DemoFile -RelativePath "Archive\old_backup_202$((3 + $n % 5))_file$n.bak" -Content "Old backup content (fictional), file $n."
}

# A hidden file to exercise the "include hidden files" scan option
$hidden = Join-Path $TargetDir "Documents\hidden_note.txt"
[System.IO.File]::WriteAllText($hidden, "This file is hidden (demo data).")
(Get-Item $hidden).Attributes = (Get-Item $hidden).Attributes -bor [System.IO.FileAttributes]::Hidden

$total = (Get-ChildItem $TargetDir -Recurse -File).Count
Write-Host ""
Write-Host "Demo data created at: $TargetDir"
Write-Host "Total files: $total"
Write-Host ""
Write-Host "Next steps:"
Write-Host "  1. Start FileFlow"
Write-Host "  2. Files page -> Browse -> select: $TargetDir"
Write-Host "  3. Press Scan"
