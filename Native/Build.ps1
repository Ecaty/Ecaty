param([ValidateSet('Auto','MSVC','GCC')][string]$Compiler='Auto')
$ErrorActionPreference='Stop'
$nativeRoot=$PSScriptRoot
$output=Join-Path $nativeRoot '../Assets/Plugins/Flecs/x86_64'
New-Item -ItemType Directory -Path $output -Force | Out-Null
if (!(Test-Path -LiteralPath (Join-Path $nativeRoot 'Core/vendor/flecs.c'))) {
    throw 'Missing Core submodule. Run git submodule update --init --recursive.'
}
if ($Compiler -eq 'GCC' -or ($Compiler -eq 'Auto' -and (Get-Command gcc -ErrorAction SilentlyContinue) -and (Get-Command g++ -ErrorAction SilentlyContinue))) {
    $objects=Join-Path $nativeRoot 'build-gcc'
    New-Item -ItemType Directory -Path $objects -Force | Out-Null
    if (!(Get-Command gcc -ErrorAction SilentlyContinue) -or !(Get-Command g++ -ErrorAction SilentlyContinue)) {
        throw 'MinGW-w64 needs both gcc (upstream Flecs C) and g++ (our C++17 simulation).'
    }
    $build=Join-Path $nativeRoot 'build-mingw'
    & cmake -S $nativeRoot -B $build -G 'MinGW Makefiles' -DCMAKE_BUILD_TYPE=Release "-DUNITY_PLUGIN_DIR=$([IO.Path]::GetFullPath($output))"
    if($LASTEXITCODE -ne 0){throw 'MinGW CMake configuration failed.'}
    & cmake --build $build --parallel 4
    if($LASTEXITCODE -ne 0){throw 'MinGW native build failed.'}
} else {
    & (Join-Path $nativeRoot 'Generate-Solution.ps1') -Build -Configuration Release
}
Write-Output "Built: $output/flecs_unity.dll"
