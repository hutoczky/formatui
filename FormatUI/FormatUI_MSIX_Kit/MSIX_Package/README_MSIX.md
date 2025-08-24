# FormatUI — MSIX csomagolás (quickstart)

Dátum: 2025-08-24

## 0) Előkészítés
- A futtatható projekt binárisa: `FormatUI\FormatUI.exe` (x64 Release).
- Ikonok: `Assets\AppIcon.ico`, Visual Assets PNG-k generálva.

## 1) Packaging projekt (VS-ben)
1. **Add > New Project > Windows Application Packaging Project** (C#).
2. Név: `FormatUI.Package`. Target: x64, Windows 10/11.
3. Jobb klikk a packaging projekten **Add Reference...** > válaszd a FormatUI (desktop) projektet.
4. **Package.appxmanifest**: cseréld a tartalmát a mellékelt fájlra.
5. **Visual Assets**: hivatkozz az `Assets` mappára (Square44x44, Square150, Square256, StoreLogo).
6. (Opcionális) Állíts ablak ikont a desktop projekten: `Assets\AppIcon.ico` + `appWindow?.SetIcon("Assets/AppIcon.ico");`

## 2) Aláírás
- Ha nem a Store-ba megy: **MakeCert** helyett új módszer:
  PowerShell (admin):
  ```powershell
  $cert = New-SelfSignedCertificate -Type CodeSigningCert -Subject "CN=GamesTech" -KeyExportPolicy Exportable -CertStoreLocation Cert:\CurrentUser\My
  $pwd = ConvertTo-SecureString -String "P@ssw0rd!" -Force -AsPlainText
  Export-PfxCertificate -Cert $cert -FilePath .\GamesTech_CodeSigning.pfx -Password $pwd
  ```
- A packaging projekt **Signing** lapján add meg a `.pfx`-et és a jelszót.

## 3) Build
- Configuration: **Release | x64**.
- Jobb klikk a packaging projekten: **Publish > Create App Packages...** > (Sideloading) > `FormatUI_1.0.0.0_x64.msixbundle`.
- Telepítés: duplakatt; ha kell, **Install certificate** a megbízható tárolóba.

## Megjegyzés
- A `Package.appxmanifest` `Executable="FormatUI\FormatUI.exe"` útvonala a Packaging projekt kimenetére vonatkozik (automatikus bepakolás történik a hivatkozás miatt).
- Ha más mappanév: módosítsd az elérési utat.
