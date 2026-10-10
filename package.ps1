param([switch]$SkipBuild)
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
if(!$SkipBuild){& (Join-Path $root 'build.ps1') -Test}
$exe=Join-Path $root 'dist\OmenLCDControl.exe'
if(!(Test-Path -LiteralPath $exe)){throw 'Build the application before packaging.'}
$version=[Diagnostics.FileVersionInfo]::GetVersionInfo($exe).FileVersion
$version=($version.Split('.')[0..2] -join '.')
$release=Join-Path $root 'release'
New-Item -ItemType Directory -Force $release | Out-Null
$stage=Join-Path ([IO.Path]::GetTempPath()) ('OmenLCDControl-package-'+[guid]::NewGuid())
$portable=Join-Path $stage 'portable\OmenLCDControl'
$source=Join-Path $stage 'source\OmenLCDControl'
New-Item -ItemType Directory -Force $portable,$source | Out-Null
Copy-Item -LiteralPath $exe -Destination $portable
foreach($name in @('README.md','LICENSE','CHANGELOG.md','CONTRIBUTING.md','docs','examples')){
 Copy-Item -LiteralPath (Join-Path $root $name) -Destination $portable -Recurse
}
Get-ChildItem -LiteralPath $root -Force | Where-Object {$_.Name -notmatch '^(dist($|-)|release$|bin$|obj$|\.vs$|\.git$)'} | Copy-Item -Destination $source -Recurse -Force
# Compress-Archive omits hidden entries on some Windows versions; ZipFile preserves GitHub configuration.
Add-Type -AssemblyName System.IO.Compression.FileSystem
foreach($kind in @('windows-x64','source')){
 $destination=Join-Path $release "OmenLCDControl-v$version-$kind.zip"
 if(Test-Path -LiteralPath $destination){Remove-Item -LiteralPath $destination}
 $folder=if($kind -eq 'source'){Join-Path $stage 'source'}else{Join-Path $stage 'portable'}
 [IO.Compression.ZipFile]::CreateFromDirectory($folder,$destination)
 Write-Host "Packaged $destination"
}
# Keep the uniquely named staging folder for inspection; no user state is included.
