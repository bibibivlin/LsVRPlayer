param([string]$UnityEditor='C:\Program Files\Unity\Hub\Editor\6000.3.23f1\Editor', [switch]$SkipJava)
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$android=Join-Path $UnityEditor 'Data/PlaybackEngines/AndroidPlayer'
$java=Join-Path $android 'OpenJDK/bin/java.exe'
$native=Join-Path $root 'Native/Android'
$env:ANDROID_HOME=Join-Path $android 'SDK'
$env:JAVA_HOME=Join-Path $android 'OpenJDK'
function Copy-RuntimePlugin([string]$source,[string]$destination){
    if((Test-Path -LiteralPath $destination) -and (Get-FileHash -LiteralPath $source).Hash -eq (Get-FileHash -LiteralPath $destination).Hash){return}
    # Replace by rename: Unity may memory-map the previous file while the Editor is open.
    $staged=$destination+'.new'
    try{
        Copy-Item -LiteralPath $source -Destination $staged
        Move-Item -LiteralPath $staged -Destination $destination -Force
    }
    finally{if(Test-Path -LiteralPath $staged){Remove-Item -LiteralPath $staged}}
}
if(-not $SkipJava){
    & python (Join-Path $PSScriptRoot 'Build-AudioDecoder.py') --unity-editor $UnityEditor
    if($LASTEXITCODE -ne 0){throw 'LGPL audio decoder build failed'}
    & $java '-classpath' (Join-Path $android 'Tools/gradle/lib/*') org.gradle.launcher.GradleMain -p $native assembleRelease writeRuntimeInventory --console=plain
    if($LASTEXITCODE -ne 0){throw 'Android library build failed'}
    $pluginDir=Join-Path $root 'Assets/Plugins/Android'
    $null=New-Item -ItemType Directory -Force -Path $pluginDir
    Copy-RuntimePlugin (Join-Path $native 'build/outputs/aar/vrplayer-android-release.aar') (Join-Path $pluginDir 'vrplayer-android.aar')
    Copy-RuntimePlugin (Join-Path $root 'Native/FFmpeg/decoder/build/outputs/aar/decoder-ffmpeg-release.aar') (Join-Path $pluginDir 'vrplayer-ffmpeg-lgpl.aar')
}
$clang=Join-Path $android 'NDK/toolchains/llvm/prebuilt/windows-x86_64/bin/clang++.exe'
$libDir=Join-Path $root 'Assets/Plugins/Android/arm64-v8a'
$null=New-Item -ItemType Directory -Force -Path $libDir
$staging=Join-Path $PSScriptRoot '.runtime/native-build'
$null=New-Item -ItemType Directory -Force -Path $staging
$renderer=Join-Path $staging 'libvrplayerrender.so'
& $clang '--target=aarch64-linux-android29' '-std=c++17' '-fPIC' '-shared' '-O2' '-static-libstdc++' '-Wl,-z,max-page-size=16384' (Join-Path $root 'Native/Rendering/VideoTexture.cpp') '-lGLESv3' '-llog' '-o' $renderer
if($LASTEXITCODE -ne 0){throw 'Native rendering build failed'}
Copy-RuntimePlugin $renderer (Join-Path $libDir 'libvrplayerrender.so')
if(-not $SkipJava){
    & python (Join-Path $PSScriptRoot 'Build-LicenseNotices.py')
    if($LASTEXITCODE -ne 0){throw 'Runtime license inventory failed'}
    & python (Join-Path $PSScriptRoot 'Sync-LegalDocuments.py')
    if($LASTEXITCODE -ne 0){throw 'Offline legal document synchronization failed'}
}
Write-Output 'Native player plugins built successfully.'
