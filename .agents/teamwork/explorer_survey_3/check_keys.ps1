$content = Get-Content 'Services\LocalizationService.cs'
$currentLang = ""
$keysByLang = @{}

foreach ($line in $content) {
    if ($line -match '\["(zh-TW|zh-CN|en-US|ja-JP)"\]\s*=\s*new') {
        $currentLang = $matches[1]
        $keysByLang[$currentLang] = [System.Collections.Generic.List[string]]::new()
    }
    elseif ($currentLang -ne "" -and $line -match '^\s*\["([^"]+)"\]') {
        $key = $matches[1]
        $keysByLang[$currentLang].Add($key)
    }
}

foreach ($lang in $keysByLang.Keys) {
    Write-Output "$lang count: $($keysByLang[$lang].Count)"
}

$twKeys = $keysByLang["zh-TW"]
foreach ($lang in @("zh-CN", "en-US", "ja-JP")) {
    $other = $keysByLang[$lang]
    $missing = $twKeys | Where-Object { -not $other.Contains($_) }
    if ($missing) {
        Write-Output "Missing in $lang : $($missing -join ', ')"
    } else {
        Write-Output "$lang has all zh-TW keys"
    }
}
