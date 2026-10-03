$xamlFiles = Get-ChildItem -Path Pages,Controls,MainWindow.xaml -Recurse -Filter *.xaml
$hardcodedXaml = @()

foreach ($file in $xamlFiles) {
    $lines = Get-Content $file.FullName
    for ($i = 0; $i -lt $lines.Count; $i++) {
        $line = $lines[$i]
        # Match Text="...", Header="...", Content="...", Title="...", ToolTip="..."
        if ($line -match '(Text|Header|Content|Title|ToolTip|PlaceholderText|OnContent|OffContent)="([^"{}]*[\u4e00-\u9fa5][^"{}]*)"' -or
            $line -match '(Text|Header|Content|Title|ToolTip|PlaceholderText|OnContent|OffContent)="([^"{}]*\([A-Za-z ]+\)[^"{}]*)"') {
            $prop = $matches[1]
            $val = $matches[2]
            $hardcodedXaml += [PSCustomObject]@{
                File = $file.FullName.Replace('C:\Users\Atszl\Desktop\DiskMasterWinUI\', '')
                Line = $i + 1
                Property = $prop
                Value = $val
            }
        }
    }
}

Write-Output "Total hardcoded Chinese/Bilingual strings in XAML: $($hardcodedXaml.Count)"
$hardcodedXaml | Group-Object File | ForEach-Object {
    Write-Output "$($_.Name): $($_.Count) instances"
}
