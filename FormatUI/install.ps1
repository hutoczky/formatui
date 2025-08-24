param(
  [string]$TargetPath = "C:\Tools\FormatUI"
)

# Mappa létrehozása és fájlok másolása
New-Item -Path $TargetPath -ItemType Directory -Force | Out-Null
Copy-Item -Path ".\publish\*" -Destination $TargetPath -Recurse -Force

# Kontextusmenü telepítése
cd