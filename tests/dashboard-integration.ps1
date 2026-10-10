param([switch]$Hardware,[string]$AppPath)
$ErrorActionPreference='Stop'
$root=Split-Path $PSScriptRoot
if(!$AppPath){$AppPath=Join-Path $root 'dist\OmenLCDControl.exe'}
[void][Reflection.Assembly]::LoadFrom((Resolve-Path $AppPath))
Add-Type -AssemblyName System.Drawing
$hub=New-Object OmenLCDControl.MetricHub
$sensors=New-Object OmenLCDControl.SystemMetrics($hub)
try{
 $sensors.Poll();Start-Sleep -Milliseconds 1100;$sensors.Poll()
 $snapshot=$hub.Snapshot()
 foreach($id in @('cpu.usage','memory.usage','cpu.clock','gpu.usage','gpu.temperature','gpu.clock','gpu.memory')){
  $reading=$snapshot[$id]
  Write-Host "$id = $($reading.Value) $($reading.Unit) [$($reading.Source)]"
 }
 if($null -eq $snapshot['memory.usage'].Value -or $null -eq $snapshot['cpu.usage'].Value){throw 'Windows metrics unavailable.'}
 Write-Host $sensors.SensorStatus
 Write-Host $sensors.GpuStatus
 if($Hardware){
  $backend=New-Object OmenLCDControl.HpDisplayBackend
  if($backend.HasBackup){throw 'Restore the active Omen LCD control session before hardware tests.'}
  $share=[OmenLCDControl.HpDisplayBackend]::DefaultShare
  $names=@('globallcdsettings.json','profilelcdsettings.json')
  $before=@{};foreach($name in $names){$before[$name]=(Get-FileHash (Join-Path $share $name)).Hash}
  $folder=Join-Path $env:TEMP ('OmenLCDControl-dashboard-'+[guid]::NewGuid())
  New-Item -ItemType Directory $folder | Out-Null
  $frame=Join-Path $folder 'live.jpg'
  $config=New-Object OmenLCDControl.DashboardConfig
  $publisher=New-Object OmenLCDControl.CustomMetricServer($hub,0,'integration-only')
  function Save-Frame($value){
   [void]$publisher.Ingest(('{"metrics":[{"id":"custom.fps","label":"Game FPS","unit":"fps","value":'+$value+',"ttlSeconds":30}]}'))
   $image=[OmenLCDControl.DashboardRenderer]::Render($config,$hub.Snapshot(),0,[DateTime]::UtcNow)
   try{[OmenLCDControl.DashboardRenderer]::SaveAtomic($image,$frame)}finally{$image.Dispose()}
  }
  $output=Join-Path $share 'LCD_Image_Output.jpg'
  function Read-FrameHash {for($retry=0;$retry -lt 40;$retry++){try{return (Get-FileHash $output -ErrorAction Stop).Hash}catch{Start-Sleep -Milliseconds 50}};throw 'Cannot sample HP output.'}
  try{
   Save-Frame 111;$backend.Apply($frame,25);Start-Sleep -Seconds 3
   $first=Read-FrameHash;$sourceFirst=(Get-FileHash $frame).Hash
   Save-Frame 222;Start-Sleep -Milliseconds 1500
   $second=Read-FrameHash
   if($sourceFirst -eq (Get-FileHash $frame).Hash){throw 'Dashboard publisher did not change the source JPEG.'}
   $settings=Get-Content (Join-Path $share 'globallcdsettings.json') -Raw | ConvertFrom-Json
   if(!($settings.'$values' | Where-Object {$_.LCDEffectMode -eq 'Image' -and $_.CurrentPath.'$values' -contains $frame})){throw 'HP helper configuration does not point to the live dashboard.'}
   if(!(Get-Process OMENLCDDisplayHelper -ErrorAction SilentlyContinue)){throw 'HP helper is not running.'}
   Write-Host 'PASS changing dashboard frames published to the configured live path; HP helper running'
   if($first -ne $second){Write-Host 'PASS HP image output also changes'}else{Write-Host 'NOTE this helper transport does not expose refreshed JPEG output; physical LCD verification remains necessary.'}
  }finally{
   $backend.Restore()
   foreach($name in $names){if($before[$name] -ne (Get-FileHash (Join-Path $share $name)).Hash){throw "Restore mismatch: $name"}}
   Write-Host 'PASS original HP configuration restored byte-for-byte'
  }
 }
}finally{$sensors.Dispose()}
