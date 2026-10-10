param([switch]$Test,[string]$OutputDirectory='dist')
$ErrorActionPreference='Stop'
$root=$PSScriptRoot
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(!(Test-Path -LiteralPath $compiler)){throw '.NET Framework 4.x C# compiler not found. Install the .NET Framework 4.8 developer tools or build the project with Visual Studio.'}
$destination=Join-Path $root $OutputDirectory
New-Item -ItemType Directory -Force $destination | Out-Null
$sources=@(Get-ChildItem (Join-Path $root 'src') -Filter '*.cs' | ForEach-Object FullName)
$references=@('System.dll','System.Core.dll','System.Drawing.dll','System.Windows.Forms.dll','System.Web.Extensions.dll','System.Management.dll')
$arguments=@('/nologo','/target:winexe','/platform:x64','/optimize+','/langversion:5',('/win32manifest:'+(Join-Path $root 'app.manifest')),('/out:'+(Join-Path $destination 'OmenLCDControl.exe')))
foreach($reference in $references){$arguments+=('/reference:'+$reference)}
& $compiler @arguments @sources
if($LASTEXITCODE -ne 0){throw 'Compilation failed.'}
Write-Host "Built $destination\OmenLCDControl.exe"
if($Test){
 $report=Join-Path $destination 'test-results.txt'
 $process=Start-Process -FilePath (Join-Path $destination 'OmenLCDControl.exe') -ArgumentList @('--self-test',('"'+$report+'"')) -Wait -PassThru -WindowStyle Hidden
 Get-Content -LiteralPath $report
 if($process.ExitCode -ne 0){throw 'Self-tests failed.'}
}
