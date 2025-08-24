$baseKey = "HKCU:\Software\Classes\Drive\shell\ModernFormat"
try {
    if (Test-Path $baseKey) {
        Remove-Item -Path $baseKey -Recurse -Force
        Write-Host "Eltávolítva: $baseKey"
    } else {
        Write-Host "Nem volt telepítve."
    }
}
catch {
    Write-Host "Hiba: $($_.Exception.Message)"
}