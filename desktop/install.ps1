<#
.SYNOPSIS
  Builds the Windows app from this checkout and installs it on this PC, upgrading the copy already installed.

.DESCRIPTION
  Run from the repository root with `bun run desktop:install`, or
    powershell -NoProfile -ExecutionPolicy Bypass -File desktop\install.ps1

  Each run:
    1. Signs with a development certificate kept in %LOCALAPPDATA%\Reelshelf\dev-signing (made on first use, or when
       it expires or the manifest's publisher changes). Windows has to trust it before it installs the package, which
       needs administrator rights, so the first run (and only that one) shows a UAC prompt.
    2. Numbers the build one above the installed version (0.1.3 over 0.1.2), since Windows won't install the same
       version over itself; with nothing installed it uses the manifest's major and minor, so 0.1.0.
    3. Builds the MSIX with Reelshelf.Uploader.Package\package.ps1, installs it (closing the running app first), and
       starts it. Settings, the sign-in and the record of uploaded files are kept across upgrades.

  A Store install of the app is signed by the Store and can't be upgraded by a local build; remove it first.

.PARAMETER NoLaunch
  Install without starting the app afterwards.
#>
param([switch] $NoLaunch)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$package = Join-Path $PSScriptRoot "Reelshelf.Uploader.Package"
[xml] $manifest = Get-Content (Join-Path $package "Package.appxmanifest") -Raw
$name = $manifest.Package.Identity.Name
$publisher = $manifest.Package.Identity.Publisher
$appId = $manifest.Package.Applications.Application.Id

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw "The .NET SDK isn't installed. Install it with: winget install Microsoft.DotNet.SDK.10"
}

# 1. The development certificate, kept outside the repository and reused so Windows trusts it once.
$certificates = Join-Path $env:LOCALAPPDATA "Reelshelf\dev-signing"
$pfx = Join-Path $certificates "dev-signing.pfx"
$cer = Join-Path $certificates "dev-signing.cer"
$passwordFile = Join-Path $certificates "dev-signing-password.txt"

function Read-Certificate {
    if (-not ((Test-Path $pfx) -and (Test-Path $cer) -and (Test-Path $passwordFile))) { return $null }
    $password = Get-Content $passwordFile -Raw
    $certificate = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2($pfx, $password)
    if ($certificate.Subject -ne $publisher -or $certificate.NotAfter -lt (Get-Date).AddDays(7)) { return $null }
    return $certificate
}

$certificate = Read-Certificate
if (-not $certificate) {
    Write-Host "Making a development signing certificate for $publisher"
    New-Item -ItemType Directory -Force -Path $certificates | Out-Null
    $made = New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature `
        -FriendlyName "Reelshelf Windows app (local development)" -CertStoreLocation "Cert:\CurrentUser\My" `
        -TextExtension @("2.5.29.37={text}1.3.6.1.5.5.7.3.3", "2.5.29.19={text}") -NotAfter (Get-Date).AddYears(2)
    $password = [Guid]::NewGuid().ToString("N")
    Export-PfxCertificate -Cert $made -FilePath $pfx -Password (ConvertTo-SecureString -String $password -AsPlainText -Force) | Out-Null
    Export-Certificate -Cert $made -FilePath $cer | Out-Null
    Set-Content -Path $passwordFile -Value $password -NoNewline
    Remove-Item "Cert:\CurrentUser\My\$($made.Thumbprint)"
    $certificate = Read-Certificate
}

if (-not (Test-Path "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)")) {
    Write-Host "Asking Windows to trust the development certificate (a UAC prompt; only needed once)"
    $import = "Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople | Out-Null"
    Start-Process powershell -Verb RunAs -Wait -ArgumentList "-NoProfile", "-Command", $import
    if (-not (Test-Path "Cert:\LocalMachine\TrustedPeople\$($certificate.Thumbprint)")) {
        throw "The certificate wasn't trusted, so Windows won't install the package. Run this again and accept the UAC prompt."
    }
}

# 2. One version above what's installed.
$installed = Get-AppxPackage -Name $name | Select-Object -First 1
if ($installed -and $installed.SignatureKind -eq "Store") {
    throw "The Store's copy of the app is installed, and a local build can't upgrade it. Remove it first: Get-AppxPackage $name | Remove-AppxPackage"
}

if ($installed) {
    $current = [version] $installed.Version
    $version = "$($current.Major).$($current.Minor).$($current.Build + 1)"
}
else {
    $base = [version] $manifest.Package.Identity.Version
    $version = "$($base.Major).$($base.Minor).0"
}

# 3. Build, install, start.
$password = Get-Content $passwordFile -Raw
& (Join-Path $package "package.ps1") -Version $version -Mode Sideload -CertificatePath $pfx -CertificatePassword $password

$msix = Join-Path $package "out\Reelshelf.Uploader_$($version)_x64.msix"
Write-Host "Installing $version"
Add-AppxPackage -Path $msix -ForceApplicationShutdown

if (-not $NoLaunch) {
    $family = (Get-AppxPackage -Name $name | Select-Object -First 1).PackageFamilyName
    Start-Process "shell:AppsFolder\$family!$appId"
}

Write-Host "Installed Reelshelf $version"
