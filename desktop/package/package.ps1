<#
.SYNOPSIS
  Builds Reelshelf's MSIX: builds the app (desktop/app, crate reelshelf-app) for x86_64-pc-windows-msvc with cargo,
  lays it out as Reelshelf.exe with the manifest and its assets, and packs it with makeappx into
  out\Reelshelf_<version>_x64.msix.

.DESCRIPTION
  -Mode Store (what publish-uploader.yml uses) leaves the package unsigned: the Store signs it.
  -Mode Sideload (the default) signs it with a throwaway self-signed certificate for testing on this machine only. To
  install that package, trust the certificate first, from an elevated PowerShell:
    Import-PfxCertificate -FilePath out\sideload.pfx -CertStoreLocation Cert:\LocalMachine\TrustedPeople -Password (ConvertTo-SecureString -String (Get-Content out\sideload-password.txt) -AsPlainText -Force)
  then double-click the .msix, or run Add-AppxPackage out\Reelshelf_<version>_x64.msix.
  -CertificatePath (with -CertificatePassword) signs with that certificate instead, as desktop/install.ps1 does with
  one it keeps, so it is trusted once rather than on every build.

  Needs the Rust toolchain (rustup, which reads desktop/rust-toolchain.toml) with Visual Studio's C++ build tools, and
  the Windows SDK for makeappx and signtool.

.EXAMPLE
  ./package.ps1 -Version 1.0.3 -Mode Store
#>
param(
    [string] $Version = "0.1.0",
    [ValidateSet("Sideload", "Store")] [string] $Mode = "Sideload",
    [string] $CertificatePath,
    [string] $CertificatePassword
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

if ($Version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Version must be <major>.<minor>.<patch>, such as 1.0.3; got '$Version'."
}

$desktop = (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
$target = "x86_64-pc-windows-msvc"
$out = Join-Path $PSScriptRoot "out"
$layout = Join-Path $out "layout"
$msix = Join-Path $out "Reelshelf_$($Version)_x64.msix"

if (Test-Path $out) { Remove-Item -Recurse -Force $out }
New-Item -ItemType Directory -Path $layout | Out-Null

if (-not (Get-Command cargo -ErrorAction SilentlyContinue)) {
    throw "cargo isn't installed. Install Rust with: winget install Rustlang.Rustup"
}

# The version is compiled in (X-Reelshelf-Client, S5) and stamped on the executable (build.rs).
Write-Host "Building Reelshelf $Version (release, $target)"
$env:REELSHELF_APP_VERSION = $Version
Push-Location $desktop
try {
    cargo build --release --locked -p reelshelf-app --target $target
    if ($LASTEXITCODE -ne 0) { throw "cargo build failed" }
}
finally {
    Pop-Location
    Remove-Item Env:REELSHELF_APP_VERSION -ErrorAction SilentlyContinue
}
Copy-Item (Join-Path $desktop "target/$target/release/reelshelf.exe") (Join-Path $layout "Reelshelf.exe")

# The manifest, with the package version (MSIX versions have four parts; the Store wants the last one 0).
[xml] $manifest = Get-Content (Join-Path $PSScriptRoot "Package.appxmanifest") -Raw
$manifest.Package.Identity.Version = "$Version.0"
$manifest.Save((Join-Path $layout "AppxManifest.xml"))
Copy-Item -Recurse (Join-Path $PSScriptRoot "Assets") (Join-Path $layout "Assets")

function Find-SdkTool([string] $name) {
    $kits = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    $tool = Get-ChildItem -Path $kits -Filter $name -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object { [version] ($_.Directory.Parent.Name -replace '[^\d.]', '') } -ErrorAction SilentlyContinue |
        Select-Object -Last 1
    if (-not $tool) { throw "$name wasn't found; install the Windows 10/11 SDK." }
    return $tool.FullName
}

$makeappx = Find-SdkTool "makeappx.exe"
Write-Host "Packing $msix"
& $makeappx pack /d $layout /p $msix /o
if ($LASTEXITCODE -ne 0) { throw "makeappx pack failed" }

if ($Mode -eq "Sideload" -and $CertificatePath) {
    $signtool = Find-SdkTool "signtool.exe"
    & $signtool sign /fd SHA256 /f $CertificatePath /p $CertificatePassword $msix
    if ($LASTEXITCODE -ne 0) { throw "signtool sign failed" }
}
elseif ($Mode -eq "Sideload") {
    # A throwaway certificate whose subject matches the manifest's publisher, for local testing only. Store uploads
    # go unsigned and the Store signs them.
    $publisher = $manifest.Package.Identity.Publisher
    $certificate = New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature `
        -FriendlyName "Reelshelf sideload (test only)" -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") -NotAfter (Get-Date).AddDays(30)
    $password = [Guid]::NewGuid().ToString("N")
    $pfx = Join-Path $out "sideload.pfx"
    Export-PfxCertificate -Cert $certificate -FilePath $pfx `
        -Password (ConvertTo-SecureString -String $password -AsPlainText -Force) | Out-Null
    Set-Content -Path (Join-Path $out "sideload-password.txt") -Value $password -NoNewline
    Remove-Item "Cert:\CurrentUser\My\$($certificate.Thumbprint)"

    $signtool = Find-SdkTool "signtool.exe"
    & $signtool sign /fd SHA256 /f $pfx /p $password $msix
    if ($LASTEXITCODE -ne 0) { throw "signtool sign failed" }
    Write-Host "Signed for sideloading. Trust out\sideload.pfx (see this script's help) before installing."
}

Write-Host "Built $msix"
