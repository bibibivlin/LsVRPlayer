param([Parameter(Mandatory=$true)][string]$Json, [string]$UnityEditor='C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor', [string]$Serial='', [switch]$ReadStatus)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$adb=Join-Path $UnityEditor 'Data/PlaybackEngines/AndroidPlayer/SDK/platform-tools/adb.exe'
$package='com.linxyi.LsVRPlayer'
$deviceArgs=@();if($Serial){$deviceArgs=@('-s',$Serial)}
$null=$Json | ConvertFrom-Json
$temporary=Join-Path $root 'TestMedia/device-command.json'
[IO.File]::WriteAllText($temporary,$Json,[Text.UTF8Encoding]::new($false))
& $adb @deviceArgs push $temporary /data/local/tmp/vrplayer-command.json
if($LASTEXITCODE -ne 0){throw 'Unable to push command'}
# The dev command path is inside this app's sandbox, without an exported receiver.
& $adb @deviceArgs shell run-as $package cp /data/local/tmp/vrplayer-command.json files/command.json
if($LASTEXITCODE -ne 0){throw 'Unable to stage command; install a development APK first'}
if($ReadStatus){& $adb @deviceArgs shell cat /sdcard/Android/data/com.linxyi.LsVRPlayer/files/playback-diagnostics.json}
