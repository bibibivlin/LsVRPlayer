param([string]$UnityEditor='C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor', [switch]$SkipJava)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$android=Join-Path $UnityEditor 'Data/PlaybackEngines/AndroidPlayer'
$java=Join-Path $android 'OpenJDK/bin/java.exe'
$native=Join-Path $root 'Native/Android'
$env:ANDROID_HOME=Join-Path $android 'SDK'
$env:JAVA_HOME=Join-Path $android 'OpenJDK'
if(-not $SkipJava){
    & $java '-classpath' (Join-Path $android 'Tools/gradle/lib/*') org.gradle.launcher.GradleMain -p $native assembleRelease --console=plain
    if($LASTEXITCODE -ne 0){throw 'Android library build failed'}
    $pluginDir=Join-Path $root 'Assets/Plugins/Android'
    $null=New-Item -ItemType Directory -Force -Path $pluginDir
    Copy-Item -LiteralPath (Join-Path $native 'build/outputs/aar/vrplayer-android-release.aar') -Destination (Join-Path $pluginDir 'vrplayer-android.aar')
}
$clang=Join-Path $android 'NDK/toolchains/llvm/prebuilt/windows-x86_64/bin/clang++.exe'
$libDir=Join-Path $root 'Assets/Plugins/Android/arm64-v8a'
$null=New-Item -ItemType Directory -Force -Path $libDir
& $clang '--target=aarch64-linux-android29' '-std=c++17' '-fPIC' '-shared' '-O2' '-static-libstdc++' '-Wl,-z,max-page-size=16384' (Join-Path $root 'Native/Rendering/VideoTexture.cpp') '-lGLESv3' '-llog' '-o' (Join-Path $libDir 'libvrplayerrender.so')
if($LASTEXITCODE -ne 0){throw 'Native rendering build failed'}
Write-Output 'Native player plugins built successfully.'
