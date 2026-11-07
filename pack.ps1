#Requires -Version 7
<#
.SYNOPSIS
  Builds the Usage Dock extension and produces an MSIX (no Windows SDK install needed).

.EXAMPLE
  ./pack.ps1 -Register            # dev loop: register the unpacked layout (needs Developer Mode)
  ./pack.ps1 -SelfSign -Install   # build + sign with a local dev cert + install (one UAC prompt to trust the cert)
  ./pack.ps1 -Version 1.0.0.0 -CertThumbprint ABC123...   # release build, cert from CurrentUser\My
  ./pack.ps1 -Version 1.0.0.0 -CertPath my.pfx              # release build, prompts for the pfx password
  ./pack.ps1 -Version 1.0.0.0 -Store                          # unsigned Store packages (x64 + arm64) bundled into an .msixupload
#>
param(
  [string]$Version = '0.1.0.0',
  [ValidateSet('x64', 'arm64')][string]$Platform = 'x64',
  [switch]$Register,
  [switch]$SelfSign,
  [string]$CertPath,
  [string]$CertThumbprint,
  [switch]$Install,
  # Microsoft Store build: uses the identity Partner Center assigned, leaves the package unsigned
  # (the Store signs it), builds both architectures and bundles them for upload.
  [switch]$Store,
  [string]$StoreIdentityName = 'ArihantTripathy.UsageDockforClaudeCode',
  [string]$StorePublisher = 'CN=72BAF5FF-24DB-44E5-91AB-EF577484B99F',
  [string]$StorePublisherDisplayName = 'Arihant Tripathy'
)
$ErrorActionPreference = 'Stop'
$root    = $PSScriptRoot

if ($Store -and -not $env:USAGEDOCK_STORE_CHILD) {
  if ($Version -notmatch '\.0$') { throw 'Store package versions must end in .0' }
  $env:USAGEDOCK_STORE_CHILD = '1'
  try {
    $parts = foreach ($p in 'x64', 'arm64') {
      & $PSCommandPath -Version $Version -Platform $p -Store | Out-Host
      Join-Path $root "out/$p/ClaudeUsageDock_${Version}_$p.msix"
    }
  } finally { Remove-Item Env:USAGEDOCK_STORE_CHILD }
  $storeOut = Join-Path $root 'out/store'
  Remove-Item $storeOut -Recurse -Force -ErrorAction SilentlyContinue
  New-Item -ItemType Directory -Force (Join-Path $storeOut 'parts') | Out-Null
  $parts | Copy-Item -Destination (Join-Path $storeOut 'parts')
  $nuget = $env:NUGET_PACKAGES ?? (Join-Path $HOME '.nuget/packages')
  $makeappx = (Get-ChildItem (Join-Path $nuget 'microsoft.windows.sdk.buildtools') -Recurse -Filter makeappx.exe |
    Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1).FullName
  $bundle = Join-Path $storeOut "UsageDock_$Version.msixbundle"
  & $makeappx bundle /o /bv $Version /d (Join-Path $storeOut 'parts') /p $bundle
  if ($LASTEXITCODE) { throw 'makeappx bundle failed' }
  $upload = [IO.Path]::ChangeExtension($bundle, '.msixupload')
  Compress-Archive -Path $bundle -DestinationPath ([IO.Path]::ChangeExtension($bundle, '.zip')) -Force
  Move-Item ([IO.Path]::ChangeExtension($bundle, '.zip')) $upload -Force
  Write-Host "Store upload: $upload"
  return
}
$proj    = Join-Path $root 'ClaudeUsage/ClaudeUsage.csproj'
$out     = Join-Path $root "out/$Platform"
$layout  = Join-Path $out 'layout'
$msix    = Join-Path $out "ClaudeUsageDock_${Version}_$Platform.msix"

# 1. Publish
Remove-Item $layout -Recurse -Force -ErrorAction SilentlyContinue
dotnet publish $proj -c Release -r "win-$Platform" -p:Platform=$Platform -p:PublishProfile="win-$Platform" -p:DebugType=none -o $layout
if ($LASTEXITCODE) { throw 'dotnet publish failed' }
Get-ChildItem $layout -Recurse -Filter *.pdb | Remove-Item

# 2. AppxManifest.xml (fill the tokens Visual Studio's MSIX tooling normally fills)
$manifest = Get-Content (Join-Path $root 'ClaudeUsage/Package.appxmanifest') -Raw
$manifest = $manifest `
  -replace '\$targetnametoken\$', 'ClaudeUsage' `
  -replace '\$targetentrypoint\$', 'Windows.FullTrustApplication' `
  -replace '<Resource Language="x-generate"\s*/>', '<Resource Language="en-us" />' `
  -replace '(<Identity[^>]*?Version=")[^"]+"', "`${1}$Version`" ProcessorArchitecture=`"$Platform`"" `
  -replace 'MaxVersionTested="[^"]+"', 'MaxVersionTested="10.0.26100.0"' `
  -replace '(Square150x150Logo|Square44x44Logo|Wide310x150Logo|SplashScreen).png', '$1.scale-200.png'
if ($Store) {
  $manifest = $manifest `
    -replace '(<Identity\s+Name=")[^"]+"', "`${1}$StoreIdentityName`"" `
    -replace '(<Identity[^>]*?Publisher=")[^"]+"', "`${1}$StorePublisher`"" `
    -replace '<PublisherDisplayName>[^<]*</PublisherDisplayName>', "<PublisherDisplayName>$StorePublisherDisplayName</PublisherDisplayName>"
}
Set-Content (Join-Path $layout 'AppxManifest.xml') $manifest -Encoding utf8
New-Item -ItemType Directory -Force (Join-Path $layout 'Public') | Out-Null

if ($Register) {
  Add-AppxPackage -Register (Join-Path $layout 'AppxManifest.xml') -ForceApplicationShutdown
  Write-Host 'Registered. Reload Command Palette (or restart PowerToys) to pick it up.'
  return
}

# 3. Pack
$nuget = $env:NUGET_PACKAGES ?? (Join-Path $HOME '.nuget/packages')
$tools = Get-ChildItem (Join-Path $nuget 'microsoft.windows.sdk.buildtools') -Recurse -Filter makeappx.exe |
  Where-Object { $_.Directory.Name -eq 'x64' } | Sort-Object FullName -Descending | Select-Object -First 1
if (-not $tools) { throw 'makeappx.exe not found; run dotnet restore first' }
$makeappx = $tools.FullName
$signtool = Join-Path $tools.DirectoryName 'signtool.exe'

& $makeappx pack /o /d $layout /p $msix
if ($LASTEXITCODE) { throw 'makeappx failed' }

# 4. Sign (the certificate subject must equal the manifest Publisher or Windows refuses the package)
$publisher = ([xml]$manifest).Package.Identity.Publisher
function Assert-Subject($cert) {
  if ($cert.Subject -ne $publisher) { throw "Certificate subject '$($cert.Subject)' does not match manifest Publisher '$publisher'." }
}
if ($SelfSign) {
  $cert = Get-ChildItem Cert:\CurrentUser\My | Where-Object { $_.Subject -eq $publisher -and $_.NotAfter -gt (Get-Date) -and $_.HasPrivateKey } | Select-Object -First 1
  if (-not $cert) {
    $cert = New-SelfSignedCertificate -Type Custom -Subject $publisher -KeyUsage DigitalSignature -FriendlyName 'Usage Dock signing cert' `
      -CertStoreLocation Cert:\CurrentUser\My -NotAfter (Get-Date).AddYears(5) -TextExtension @('2.5.29.37={text}1.3.6.1.5.5.7.3.3', '2.5.29.19={text}')
  }
  $CertThumbprint = $cert.Thumbprint
  $cer = Join-Path $out 'UsageDock.cer'
  Export-Certificate -Cert $cert -FilePath $cer | Out-Null
  if ($Install -and -not (Get-ChildItem Cert:\LocalMachine\TrustedPeople | Where-Object Thumbprint -eq $cert.Thumbprint)) {
    Write-Host 'Trusting the signing cert (UAC prompt)...'
    Start-Process pwsh -Verb RunAs -Wait -ArgumentList '-NoProfile', '-Command', "Import-Certificate -FilePath '$cer' -CertStoreLocation Cert:\LocalMachine\TrustedPeople"
  }
}
if ($CertThumbprint) {
  $cert = Get-Item "Cert:\CurrentUser\My\$CertThumbprint"
  Assert-Subject $cert
  & $signtool sign /fd SHA256 /sha1 $CertThumbprint /s My $msix
  if ($LASTEXITCODE) { throw 'signtool failed' }
} elseif ($CertPath) {
  $pw = Read-Host -AsSecureString 'PFX password'
  $pfx = Get-PfxData -FilePath $CertPath -Password $pw
  Assert-Subject $pfx.EndEntityCertificates[0]
  $imported = Import-PfxCertificate -FilePath $CertPath -Password $pw -CertStoreLocation Cert:\CurrentUser\My
  try {
    & $signtool sign /fd SHA256 /sha1 $imported.Thumbprint /s My /tr http://timestamp.digicert.com /td SHA256 $msix
    if ($LASTEXITCODE) { throw 'signtool failed' }
  } finally { Remove-Item "Cert:\CurrentUser\My\$($imported.Thumbprint)" -ErrorAction SilentlyContinue }
}

if ($Install) {
  Add-AppxPackage $msix -ForceApplicationShutdown -ForceUpdateFromAnyVersion
  Write-Host 'Installed. Reload Command Palette (or restart PowerToys) to pick it up.'
}
Write-Host "Package: $msix"
