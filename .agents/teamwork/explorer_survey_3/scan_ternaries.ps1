$files = Get-ChildItem -Path Pages,Controls,ViewModels,Helpers,MainWindow.xaml.cs -Recurse -Filter *.cs
$report = @()

foreach ($file in $files) {
    $lines = Get-Content $file.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        if ($line -match 'isZh\s*\?\s*"([^"]+)"\s*:\s*"([^"]+)"' -or $line -match 'IsChinese\s*\?\s*"([^"]+)"\s*:\s*"([^"]+)"') {
            $report += [PSCustomObject]@{
                File = $file.FullName.Replace('C:\Users\Atszl\Desktop\DiskMasterWinUI\', '')
                Line = $i + 1
                Zh = $matches[1]
                En = $matches[2]
            }
        }
    }
}

Write-Output "Total hardcoded isZh / IsChinese ternary strings found: $($report.Count)"
$report | Group-Object File | ForEach-Object {
    Write-Output "$($_.Name): $($_.Count) instances"
}
