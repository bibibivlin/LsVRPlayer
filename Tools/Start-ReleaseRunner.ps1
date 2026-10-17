param([string]$RunnerDirectory=(Join-Path $env:LOCALAPPDATA 'LsVRPlayer/actions-runner'))
$ErrorActionPreference='Stop'
$runner=[IO.Path]::GetFullPath($RunnerDirectory)
$listener=Join-Path $runner 'bin/Runner.Listener.exe'
if(-not (Test-Path -LiteralPath (Join-Path $runner '.runner'))){throw 'Register this repository runner in GitHub Settings / Actions / Runners first'}
if(-not (Test-Path -LiteralPath $listener)){throw 'Runner.Listener.exe is missing'}
$existing=Get-CimInstance Win32_Process -Filter "Name = 'Runner.Listener.exe'" |
    Where-Object {$_.ExecutablePath -eq $listener}
if($existing){Write-Output 'Repository runner is already running';return}
$process=Start-Process -FilePath $listener -ArgumentList 'run' -WorkingDirectory $runner -WindowStyle Hidden -PassThru `
    -RedirectStandardOutput (Join-Path $runner 'listener.stdout.log') -RedirectStandardError (Join-Path $runner 'listener.stderr.log')
$process.Id | Set-Content -LiteralPath (Join-Path $runner 'listener.pid')
Write-Output 'Repository runner started; keep this computer online during builds.'
