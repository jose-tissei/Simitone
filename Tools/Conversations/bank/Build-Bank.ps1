# Assembles bank.json from the hand-written lines in bank/lines/*.txt. Nothing here calls a language model: the lines are written and edited by hand.
#   pwsh Tools/Conversations/bank/Build-Bank.ps1
# The "subjects" list already in bank.json is kept as it is. The format of the line files is described at the top of lines/01-*.txt.
# Tone words map to the game's numbers: gentle = 0, playful = 1, bold = 2.
param([string]$Bank = (Join-Path $PSScriptRoot 'bank.json'))
$ErrorActionPreference = 'Stop'
$tones = @{ gentle = '0'; playful = '1'; bold = '2' }
$temperaments = 'easily_offended', 'thick_skinned', 'cold', 'receptive', 'any'

$speaker = [ordered]@{}
$reply = [ordered]@{}
$current = $null
foreach ($file in Get-ChildItem (Join-Path $PSScriptRoot 'lines') -Filter '*.txt' | Sort-Object Name) {
    $n = 0
    foreach ($raw in Get-Content $file.FullName -Encoding UTF8) {
        $n++
        $line = $raw.TrimEnd()
        if ($line -eq '' -or $line.StartsWith('#')) { continue }
        if ($line -match '^\[(speaker|reply) (.+)\]$') {
            $parts = $Matches[2].Split(' ')
            if ($Matches[1] -eq 'speaker') {
                if ($parts.Count -ne 2 -or -not $tones.ContainsKey($parts[1])) { throw "$($file.Name):$n bad speaker section '$line'" }
                if (-not $speaker.Contains($parts[0])) { $speaker[$parts[0]] = [ordered]@{} }
                $list = New-Object System.Collections.Generic.List[string]
                $speaker[$parts[0]][$tones[$parts[1]]] = $list
            }
            else {
                if ($parts.Count -ne 3 -or $parts[1] -notin '1', '2', '3' -or $parts[2] -notin $temperaments) { throw "$($file.Name):$n bad reply section '$line'" }
                if (-not $reply.Contains($parts[0])) { $reply[$parts[0]] = [ordered]@{} }
                if (-not $reply[$parts[0]].Contains($parts[1])) { $reply[$parts[0]][$parts[1]] = [ordered]@{} }
                $list = New-Object System.Collections.Generic.List[string]
                $reply[$parts[0]][$parts[1]][$parts[2]] = $list
            }
            $current = $list
            continue
        }
        if ($null -eq $current) { throw "$($file.Name):$n line outside any section: $line" }
        $current.Add($line)
    }
}

$old = Get-Content $Bank -Raw -Encoding UTF8 | ConvertFrom-Json
$out = [ordered]@{ version = 1; speaker = $speaker; reply = $reply; subjects = @($old.subjects) }
# lists become arrays so a one-line list is still a JSON array
foreach ($i in $speaker.Keys) { foreach ($t in @($speaker[$i].Keys)) { $speaker[$i][$t] = [string[]]$speaker[$i][$t] } }
foreach ($i in $reply.Keys) { foreach ($q in $reply[$i].Keys) { foreach ($t in @($reply[$i][$q].Keys)) { $reply[$i][$q][$t] = [string[]]$reply[$i][$q][$t] } } }
$json = $out | ConvertTo-Json -Depth 10
$json = $json.Replace('\u' + '0027', [string][char]39).Replace('\u' + '0026', [string][char]38)   # ConvertTo-Json escapes ' and &
$json = ($json -replace "`r?`n", "`r`n") + "`r`n"
[IO.File]::WriteAllText($Bank, $json, (New-Object System.Text.UTF8Encoding($false)))
$speakerLines = ($speaker.Values | ForEach-Object { $_.Values } | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
$replyLines = ($reply.Values | ForEach-Object { $_.Values } | ForEach-Object { $_.Values } | ForEach-Object { $_.Count } | Measure-Object -Sum).Sum
Write-Host "bank.json written: $speakerLines speaker lines, $replyLines reply lines, $(@($old.subjects).Count) subjects" -ForegroundColor Green
