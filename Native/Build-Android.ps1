param(
    [string]$UnityEditor = '',
    [ValidateSet('Release','RelWithDebInfo')][string]$Configuration = 'Release'
)
# Cross-compiles flecs_unity for Android arm64-v8a with the NDK bundled in the Unity Android module
# and copies a stripped libflecs_unity.so into Assets/Plugins/Flecs/Android/arm64-v8a.
$ErrorActionPreference = 'Stop'
$nativeRoot = $PSScriptRoot
$projectRoot = (Resolve-Path (Join-Path $nativeRoot '..')).Path
if (!(Test-Path -LiteralPath (Join-Path $nativeRoot 'Core/vendor/flecs.c'))) {
    throw 'Missing Core submodule. Run git submodule update --init --recursive.'
}
if (!$UnityEditor) {
    $version = (Select-String -Path (Join-Path $projectRoot 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (.+)$').Matches[0].Groups[1].Value.Trim()
    $candidates = @("C:/Program Files/Unity/$version/Editor", "C:/Program Files/Unity/Hub/Editor/$version/Editor")
    $UnityEditor = $candidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
    if (!$UnityEditor) { throw "Unity $version not found; pass -UnityEditor <path to Editor folder>." }
}
$android = Join-Path $UnityEditor 'Data/PlaybackEngines/AndroidPlayer'
$ndk = Join-Path $android 'NDK'
$cmakeBin = Get-ChildItem -Directory (Join-Path $android 'SDK/cmake') | Sort-Object Name -Descending | Select-Object -First 1
if (!(Test-Path -LiteralPath $ndk) -or !$cmakeBin) { throw 'Unity Android module (NDK + SDK CMake) is not installed.' }
$cmake = Join-Path $cmakeBin.FullName 'bin/cmake.exe'
$ninja = Join-Path $cmakeBin.FullName 'bin/ninja.exe'
$strip = Join-Path $ndk 'toolchains/llvm/prebuilt/windows-x86_64/bin/llvm-strip.exe'

$build = Join-Path $nativeRoot 'build-android-arm64'
$staging = Join-Path $build 'out'
$output = Join-Path $projectRoot 'Assets/Plugins/Flecs/Android/arm64-v8a'
& $cmake -S $nativeRoot -B $build -G Ninja "-DCMAKE_MAKE_PROGRAM=$ninja" `
    "-DCMAKE_TOOLCHAIN_FILE=$ndk/build/cmake/android.toolchain.cmake" `
    -DANDROID_ABI=arm64-v8a -DANDROID_PLATFORM=android-24 -DANDROID_STL=c++_static `
    "-DCMAKE_BUILD_TYPE=$Configuration" "-DUNITY_PLUGIN_DIR=$staging"
if ($LASTEXITCODE -ne 0) { throw 'Android CMake configuration failed.' }
& $cmake --build $build --parallel 8
if ($LASTEXITCODE -ne 0) { throw 'Android native build failed.' }
New-Item -ItemType Directory -Path $output -Force | Out-Null
& $strip --strip-unneeded -o (Join-Path $output 'libflecs_unity.so') (Join-Path $staging 'libflecs_unity.so')
if ($LASTEXITCODE -ne 0) { throw 'llvm-strip failed.' }
Write-Output "Built: $output/libflecs_unity.so"
