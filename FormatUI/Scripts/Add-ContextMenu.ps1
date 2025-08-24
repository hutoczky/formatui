<#
.SYNOPSIS
  Jobb klikk menü bejegyzés létrehozása: "Formázás (modern)"
.PARAMETER ExePath
  A FormatUI.exe teljes elérési útja.
#>

param([string]$ExePath = "C:\Tools\FormatUI\FormatUI.exe")

$baseKey = "HKCU:\Software\Classes\Drive\shell\ModernFormat"
$cmdKey  = Join-Path $baseKey "command"

try {
  New-Item -Path $baseKey -Force | Out-Null
  New-ItemProperty -Path $baseKey -Name "MUIVerb" -Value "Formázás (modern)" -Force | Out-Null
  New-ItemProperty -Path $baseKey -Name "Icon" -Value $ExePath -Force | Out-Null
  New-ItemProperty -Path $baseKey -Name "HasLUAShield" -Value "" -Force | Out-Null
  New-Item -Path $cmdKey -Force | Out-Null
  New-ItemProperty -Path $cmdKey -Name "(default)" -Value "`"$ExePath`" `"%1`"" -Force | Out-Null
  Write-Host "Kész. Jobb klikk → Formázás (modern) elérhető."
}
catch {
  Write-Host "Hiba történt: $($_.Exception.Message)"
}