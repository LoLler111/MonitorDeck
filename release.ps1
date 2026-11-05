# Pubblica una nuova versione di Monitor Deck.
# Uso:  .\release.ps1 2.1.0
#
# Prima scrivi le novita' in CHANGELOG.md sotto "## [Non rilasciato]".
# Lo script: sposta quelle note in "## [2.1.0] - data", aggiorna AppInfo.Version, compila,
# fa il commit, crea il tag v2.1.0 e invia tutto a GitHub. GitHub Actions crea la release
# e da quel momento le app installate propongono l'aggiornamento.
param([Parameter(Mandatory = $true)][string]$Version)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot

if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw "Versione non valida: usa il formato 2.1.0" }
if (git status --porcelain -- src build.bat) { throw "Ci sono modifiche non salvate in src/: fai prima il commit (o annullale)." }
if (git tag -l "v$Version") { throw "Il tag v$Version esiste gia'." }

# CHANGELOG: [Non rilasciato] -> [x.y.z] - data
$utf8 = New-Object System.Text.UTF8Encoding $false
$cl = [IO.File]::ReadAllText("$PSScriptRoot\CHANGELOG.md", $utf8)
$m = [regex]::Match($cl, '(?s)## \[Non rilasciato\]\s*(.*?)(?=\r?\n## \[)')
if (-not $m.Success -or $m.Groups[1].Value.Trim().Length -eq 0) { throw "Scrivi prima le novita' in CHANGELOG.md sotto '## [Non rilasciato]'." }
$prev = [regex]::Match($cl, '## \[(\d+\.\d+\.\d+)\]').Groups[1].Value
$date = Get-Date -Format 'yyyy-MM-dd'
$cl = $cl.Replace($m.Value, "## [Non rilasciato]`r`n`r`n## [$Version] - $date`r`n`r`n" + $m.Groups[1].Value.Trim() + "`r`n")
$repo = 'https://github.com/LoLler111/MonitorDeck'
$cl = [regex]::Replace($cl, '\[Non rilasciato\]: [^\r\n]*', "[Non rilasciato]: $repo/compare/v$Version...HEAD`r`n[$Version]: $repo/compare/v$prev...v$Version")
[IO.File]::WriteAllText("$PSScriptRoot\CHANGELOG.md", $cl, $utf8)

# Versione nel codice
$ai = [IO.File]::ReadAllText("$PSScriptRoot\src\AppInfo.cs", $utf8)
$ai = [regex]::Replace($ai, 'Version = "[^"]+"', "Version = `"$Version`"")
[IO.File]::WriteAllText("$PSScriptRoot\src\AppInfo.cs", $ai, $utf8)

& "$PSScriptRoot\build.bat" nopause
if ($LASTEXITCODE -ne 0) { throw "Compilazione fallita" }

git add CHANGELOG.md src/AppInfo.cs
git commit -m "Versione $Version"
git tag "v$Version"
git push origin HEAD "v$Version"
Write-Host "`nFatto: v$Version inviata. La release compare tra qualche minuto su $repo/releases" -ForegroundColor Green
