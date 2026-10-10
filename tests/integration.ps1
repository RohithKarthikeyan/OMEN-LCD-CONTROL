param([switch]$Hardware)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
$app=Join-Path $root 'dist\OmenLCDControl.exe'
[void][Reflection.Assembly]::LoadFrom($app)
Add-Type -AssemblyName System.Drawing
$ffmpeg='C:\ProgramData\HP\LCDDisplayHelper\bin\ffmpeg.exe'
$ffprobe='C:\ProgramData\HP\LCDDisplayHelper\bin\ffprobe.exe'
if(!(Test-Path $ffmpeg)){throw 'Media integration tests require the HP bundled FFmpeg.'}
$fixtures=Join-Path $env:TEMP ('OmenLCDControl-integration-'+[guid]::NewGuid())
New-Item -ItemType Directory -Path $fixtures | Out-Null
$fixture=Join-Path $fixtures 'animation.gif'
& $ffmpeg -hide_banner -loglevel error -f lavfi -i 'testsrc2=size=320x240:rate=12' -t 2 -y $fixture
if($LASTEXITCODE -ne 0){throw 'Fixture generation failed.'}
$service=New-Object OmenLCDControl.MediaService($ffmpeg)
$token=[Threading.CancellationToken]::None
$preview=$service.LoadPreview($fixture,$token).GetAwaiter().GetResult()
try{
 $frames=$preview.GetFrameCount([Drawing.Imaging.FrameDimension]::Time)
 if($frames -lt 2){throw 'GIF preview lost animation frames.'}
 [void]$preview.SelectActiveFrame([Drawing.Imaging.FrameDimension]::Time,$frames-1)
 Write-Host "PASS animated GIF preview preserves $frames frames"
 $transform=New-Object OmenLCDControl.MediaTransform
 $transform.Zoom=0.75;$transform.OffsetX=65;$transform.OffsetY=-35;$transform.Fps=12
 $result=$service.Render($fixture,$transform,$preview.Width,$preview.Height,$token).GetAwaiter().GetResult()
 $probe=(& $ffprobe -v error -select_streams v:0 -show_entries stream=width,height,nb_frames -of json $result | ConvertFrom-Json).streams[0]
 if($probe.width -ne 480 -or $probe.height -ne 480 -or [int]$probe.nb_frames -lt 2){throw 'Transformed GIF export is invalid.'}
 Write-Host "PASS GIF pan/zoom export: 480x480, $($probe.nb_frames) video frames"
 $videoPreview=$service.LoadPreview($result,$token).GetAwaiter().GetResult()
 try{if($videoPreview.Width -lt 1 -or $videoPreview.Height -lt 1){throw 'Video poster preview failed.'}}finally{$videoPreview.Dispose()}
 Write-Host 'PASS video poster preview'
 $cancel=New-Object Threading.CancellationTokenSource
 $cancel.Cancel()
 $cancelled=$false
 try{$service.Render($fixture,$transform,$preview.Width,$preview.Height,$cancel.Token).GetAwaiter().GetResult() | Out-Null}catch{if($_.Exception.ToString() -match 'OperationCanceled|TaskCanceled'){$cancelled=$true}else{throw}}finally{$cancel.Dispose()}
 if(!$cancelled){throw 'Cancelled conversion completed unexpectedly.'}
 Write-Host 'PASS cancelled conversion aborts before applying'
 if($Hardware){
  $backend=New-Object OmenLCDControl.HpDisplayBackend
  if($backend.HasBackup){throw 'An existing app recovery session is active. Restore it before running hardware tests.'}
  $share=[OmenLCDControl.HpDisplayBackend]::DefaultShare
  $names=@('globallcdsettings.json','profilelcdsettings.json')
  $before=@{};foreach($name in $names){$before[$name]=(Get-FileHash (Join-Path $share $name)).Hash}
  try{
   $backend.Apply($result,12)
   Start-Sleep -Seconds 3
   $output=Join-Path $share 'LCD_Video_Output.jpg'
   function Read-FrameHash {for($retry=0;$retry -lt 50;$retry++){try{return (Get-FileHash $output -ErrorAction Stop).Hash}catch{Start-Sleep -Milliseconds 50}};throw 'Cannot sample HP output.'}
   $first=Read-FrameHash;Start-Sleep -Milliseconds 700;$second=Read-FrameHash
   if($first -eq $second){throw 'HP output frames are not changing.'}
   Write-Host 'PASS hardware helper receives changing transformed animation frames'
  }finally{
   $backend.Restore()
   foreach($name in $names){if($before[$name] -ne (Get-FileHash (Join-Path $share $name)).Hash){throw "Restore mismatch: $name"}}
   Write-Host 'PASS original HP configuration restored byte-for-byte'
  }
 }
}finally{[OmenLCDControl.MediaService]::DisposePreview($preview)}
