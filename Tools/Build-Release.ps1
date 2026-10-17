param(
    [ValidateSet('Build','Sign','All')][string]$Stage='All',
    [string]$UnityEditor='C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor',
    [string]$KeystorePath
)
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$android=Join-Path $UnityEditor 'Data/PlaybackEngines/AndroidPlayer'
$sdk=Join-Path $android 'SDK'
$env:JAVA_HOME=Join-Path $android 'OpenJDK'
$env:ANDROID_HOME=$sdk
$env:ANDROID_SDK_ROOT=$sdk
$python=$env:VRPLAYER_PYTHON
if(-not $python){$python=(& python -c 'import sys; print(sys.executable)').Trim()}
if(-not (Test-Path -LiteralPath $python)){throw 'A real Python 3.11+ executable is required; set VRPLAYER_PYTHON'}
$settings=Get-Content -LiteralPath (Join-Path $root 'ProjectSettings/ProjectSettings.asset') -Raw
$version=[regex]::Match($settings,'(?m)^  bundleVersion: (\d+\.\d+\.\d+)\s*$').Groups[1].Value
$versionCode=[regex]::Match($settings,'(?m)^  AndroidBundleVersionCode: (\d+)\s*$').Groups[1].Value
if(-not $version -or [int]$versionCode -lt 1){throw 'Invalid release version in ProjectSettings.asset'}
$staging=Join-Path $root 'Builds/.release-staging'
$release=Join-Path $root 'Builds/Release'
$statePath=Join-Path $staging 'build-state.json'
$intermediate=Join-Path $staging 'player-intermediate.apk'
$secretNames=@('ANDROID_KEYSTORE_BASE64','ANDROID_KEYSTORE_PASS','ANDROID_KEYALIAS_NAME','ANDROID_KEYALIAS_PASS')

function Invoke-UnityTask([string[]]$TaskArguments) {
    $info=New-Object Diagnostics.ProcessStartInfo
    $info.FileName=Join-Path $UnityEditor 'Unity.exe'
    $info.Arguments=($TaskArguments | ForEach-Object {'"'+$_+'"'}) -join ' '
    $info.UseShellExecute=$false
    $info.CreateNoWindow=$true
    foreach($name in $secretNames){$info.EnvironmentVariables.Remove($name)}
    $process=[Diagnostics.Process]::Start($info)
    $process.WaitForExit()
    if($process.ExitCode -ne 0){throw "Unity failed with exit code $($process.ExitCode); inspect ignored Docs/Validation/release logs"}
}

function Remove-OwnedDirectory([string]$Path,[string]$Parent) {
    $resolved=[IO.Path]::GetFullPath($Path)
    $boundary=[IO.Path]::GetFullPath($Parent).TrimEnd('\','/')+[IO.Path]::DirectorySeparatorChar
    if(-not $resolved.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase)){throw 'Cleanup target is outside its owned parent'}
    if(Test-Path -LiteralPath $resolved){Remove-Item -LiteralPath $resolved -Recurse -Force}
}

Push-Location $root
try {
    $commit=(& git rev-parse HEAD).Trim()
    if($LASTEXITCODE -ne 0){throw 'Cannot resolve source commit'}
    if($env:GITHUB_REF_TYPE -eq 'tag'){
        if($env:GITHUB_REF_NAME -cne "v$version"){throw 'Release tag must exactly match the project version'}
        & git merge-base --is-ancestor $commit origin/main
        if($LASTEXITCODE -ne 0){throw 'Release tags must refer to a commit merged into main'}
    }
    if($Stage -in @('Build','All')){
        Remove-OwnedDirectory $release (Join-Path $root 'Builds')
        Remove-OwnedDirectory $staging (Join-Path $root 'Builds')
        $null=New-Item -ItemType Directory -Path $staging -Force
        $runId=[guid]::NewGuid().ToString('N')
        $records=Join-Path $root "Docs/Validation/release/$runId"
        $null=New-Item -ItemType Directory -Path $records -Force
        $tests=Join-Path $records 'editmode-results.xml'
        & $python (Join-Path $PSScriptRoot 'Test-ReleaseVerification.py')
        if($LASTEXITCODE -ne 0){throw 'Release verification regressions failed'}
        & (Join-Path $PSScriptRoot 'Build-Native.ps1') -UnityEditor $UnityEditor
        $java=Join-Path $env:JAVA_HOME 'bin/java.exe'
        & $java '-classpath' (Join-Path $android 'Tools/gradle/lib/*') org.gradle.launcher.GradleMain -p (Join-Path $root 'Native/Android') testDebugUnitTest --console=plain
        if($LASTEXITCODE -ne 0){throw 'Native JUnit tests failed'}
        Write-Output 'Running complete VRPlayer EditMode test suite'
        Invoke-UnityTask @('-batchmode','-force-d3d11','-projectPath',$root,'-buildTarget','Android','-runTests','-testPlatform','EditMode','-assemblyNames','VRPlayer.Tests','-testResults',$tests,'-logFile',(Join-Path $records 'tests.log'))
        if(-not (Test-Path -LiteralPath $tests)){throw 'Unity did not produce test results'}
        [xml]$testReport=Get-Content -LiteralPath $tests -Raw
        if($testReport.'test-run'.result -ne 'Passed' -or [int]$testReport.'test-run'.passed -lt 1 -or [int]$testReport.'test-run'.failed -gt 0){throw 'Unity EditMode tests did not pass'}
        $env:VRPLAYER_RELEASE_INTERMEDIATE=$intermediate
        try{
            Write-Output 'Building non-development ARM64 IL2CPP player'
            Invoke-UnityTask @('-batchmode','-nographics','-quit','-projectPath',$root,'-buildTarget','Android','-executeMethod','VRPlayer.Editor.PlayerBuild.BuildAndroidReleasePlayer','-logFile',(Join-Path $records 'build.log'))
        }
        finally{Remove-Item Env:VRPLAYER_RELEASE_INTERMEDIATE -ErrorAction SilentlyContinue}
        if(-not (Test-Path -LiteralPath $intermediate)){throw 'Unity did not produce the intermediate APK'}
        @{sourceCommit=$commit;version=$version;versionCode=[int]$versionCode;tests=$tests;sha256=(Get-FileHash -LiteralPath $intermediate -Algorithm SHA256).Hash} |
            ConvertTo-Json | Set-Content -LiteralPath $statePath -Encoding UTF8
        Write-Output "Release player and tests ready: $version / $versionCode"
    }
    if($Stage -in @('Sign','All')){
        foreach($name in @('ANDROID_KEYSTORE_PASS','ANDROID_KEYALIAS_NAME','ANDROID_KEYALIAS_PASS')){
            if(-not [Environment]::GetEnvironmentVariable($name)){throw "Missing required GitHub Secret: $name"}
        }
        if(-not (Test-Path -LiteralPath $statePath)){throw 'Build stage must succeed before signing'}
        $state=Get-Content -LiteralPath $statePath -Raw | ConvertFrom-Json
        if($state.sourceCommit -ne $commit -or $state.version -ne $version -or $state.versionCode -ne [int]$versionCode -or $state.sha256 -ne (Get-FileHash -LiteralPath $intermediate -Algorithm SHA256).Hash){throw 'Intermediate build is stale or has changed'}
        $notes=Join-Path $root "Docs/Releases/v$version.zh-CN.md"
        if(-not (Test-Path -LiteralPath $notes)){throw 'Missing release notes for this version'}
        $buildTools=Get-ChildItem -LiteralPath (Join-Path $sdk 'build-tools') -Directory |
            Where-Object {$_.Name -match '^\d+\.\d+\.\d+$'} | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
        $commandTools=Get-ChildItem -LiteralPath (Join-Path $sdk 'cmdline-tools') -Directory |
            Where-Object {$_.Name -match '^\d+(\.\d+)*$'} | Sort-Object {[version]$_.Name} -Descending | Select-Object -First 1
        if(-not $buildTools -or -not $commandTools){throw 'Android SDK build-tools and cmdline-tools are required'}
        $apksigner=Join-Path $buildTools.FullName 'apksigner.bat'
        $apkanalyzer=Join-Path $commandTools.FullName 'bin/apkanalyzer.bat'
        $tempParent=[IO.Path]::GetTempPath()
        $signing=Join-Path $tempParent ('vrplayer-signing-'+[guid]::NewGuid().ToString('N'))
        $null=New-Item -ItemType Directory -Path $signing
        $completed=$false
        try{
            if($KeystorePath){$key=[IO.Path]::GetFullPath($KeystorePath)}
            else{
                if(-not $env:ANDROID_KEYSTORE_BASE64){throw 'Missing required GitHub Secret: ANDROID_KEYSTORE_BASE64'}
                $key=Join-Path $signing 'release.p12'
                [IO.File]::WriteAllBytes($key,[Convert]::FromBase64String($env:ANDROID_KEYSTORE_BASE64))
            }
            $certificatePath=Join-Path $signing 'certificate.der'
            & $python (Join-Path $PSScriptRoot 'Test-SigningKey.py') --keystore $key --certificate-out $certificatePath
            if($LASTEXITCODE -ne 0){throw 'Release key verification failed'}
            $certificate=[Security.Cryptography.X509Certificates.X509Certificate2]::new([IO.File]::ReadAllBytes($certificatePath))
            if($certificate.NotBefore -gt (Get-Date) -or $certificate.NotAfter -le (Get-Date)){throw 'Release signing certificate is outside its validity period'}
            if($certificate.Subject -match 'CN=Android Debug(?:,|$)'){throw 'Android debug certificates cannot sign a release'}
            $sha=[Security.Cryptography.SHA256]::Create()
            try{$fingerprint=([BitConverter]::ToString($sha.ComputeHash($certificate.RawData))).Replace('-','').ToLowerInvariant()}
            finally{$sha.Dispose();$certificate.Dispose()}
            if($env:VRPLAYER_SIGNING_CERT_SHA256 -and $env:VRPLAYER_SIGNING_CERT_SHA256.ToLowerInvariant().Replace(':','') -ne $fingerprint){throw 'Signing key differs from the configured release certificate'}
            Remove-OwnedDirectory $release (Join-Path $root 'Builds')
            $null=New-Item -ItemType Directory -Path $release
            $apk=Join-Path $release "LsVRPlayer-$version.apk"
            & $apksigner sign --ks $key --ks-key-alias $env:ANDROID_KEYALIAS_NAME --ks-pass env:ANDROID_KEYSTORE_PASS --key-pass env:ANDROID_KEYALIAS_PASS --out $apk $intermediate
            if($LASTEXITCODE -ne 0){throw 'APK signing failed'}
            & $python (Join-Path $PSScriptRoot 'Verify-Release.py') --apk $apk --apkanalyzer $apkanalyzer --apksigner $apksigner --expected-cert-sha256 $fingerprint --tests $state.tests --commit $commit
            if($LASTEXITCODE -ne 0){throw 'Signed release verification failed'}
            # v4 signing sidecars are not part of this standalone APK release.
            if(Test-Path -LiteralPath ($apk+'.idsig')){Remove-Item -LiteralPath ($apk+'.idsig') -Force}
            Copy-Item -LiteralPath $notes -Destination (Join-Path $release 'release-notes.md')
            $completed=$true
            Write-Output "Verified signed release: $apk"
        }
        finally{
            Remove-OwnedDirectory $signing $tempParent
            if(-not $completed){Remove-OwnedDirectory $release (Join-Path $root 'Builds')}
        }
    }
}
finally{Pop-Location}
