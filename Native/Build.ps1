param([ValidateSet('Auto','MSVC','GCC')][string]$Compiler='Auto')
$ErrorActionPreference='Stop'
$nativeRoot=$PSScriptRoot
$output=Join-Path $nativeRoot '../Assets/Plugins/Ecaty/x86_64'
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
    $common=@('-O2','-DNDEBUG','-DFLECS_SOFT_ASSERT','-DFLECS_KEEP_ASSERT','-DFLECS_CREATE_MEMBER_ENTITIES','-DFLECS_SCRIPT_MATH','-I',(Join-Path $nativeRoot 'Core/vendor'),'-I',(Join-Path $nativeRoot 'Core/include'),'-I',(Join-Path $nativeRoot 'App/include'))
    $flags=@('-std=c99') + $common
    $source=Join-Path $nativeRoot 'Core/vendor/flecs.c'
    $object=Join-Path $objects 'flecs.o'
    $header=Join-Path $nativeRoot 'Core/vendor/flecs.h'
    $stamp=Join-Path $objects 'flags.txt'
    $flagsText=$flags -join '|'
    if (!(Test-Path -LiteralPath $object) -or !(Test-Path -LiteralPath $stamp) -or (Get-Content -LiteralPath $stamp -Raw) -ne $flagsText -or (Get-Item $source).LastWriteTime -gt (Get-Item $object).LastWriteTime -or (Get-Item $header).LastWriteTime -gt (Get-Item $object).LastWriteTime) {
        & gcc @flags -c $source -o $object
        if ($LASTEXITCODE -ne 0) { throw 'Flecs compilation failed.' }
        [System.IO.File]::WriteAllText($stamp,$flagsText)
    }
    & g++ -std=c++17 @common -shared -static -static-libgcc -static-libstdc++ '-Wl,--export-all-symbols' `
      (Join-Path $nativeRoot 'Core/src/flecs_unity.cpp') (Join-Path $nativeRoot 'Core/src/Components.cpp') (Join-Path $nativeRoot 'App/src/Application.cpp') $object `
      -o (Join-Path $output 'flecs_unity.dll') -lws2_32 -ldbghelp
    if ($LASTEXITCODE -ne 0) { throw "GCC failed: $LASTEXITCODE" }
} else {
    $build=Join-Path $nativeRoot 'build'
    & cmake -S $nativeRoot -B $build -A x64
    if ($LASTEXITCODE -ne 0) { throw 'CMake configuration failed. Use an x64 MSVC developer shell.' }
    & cmake --build $build --config Release
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed.' }
}
Write-Output "Built: $output/flecs_unity.dll"
