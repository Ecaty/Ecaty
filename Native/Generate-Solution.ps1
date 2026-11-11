param(
    [switch]$Build,
    [ValidateSet('Debug','Release','RelWithDebInfo')][string]$Configuration='Release',
    [string]$PluginDirectory=''
)
$ErrorActionPreference='Stop'
$vswhere=Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio/Installer/vswhere.exe'
if (!(Test-Path -LiteralPath $vswhere)) { throw 'Install Visual Studio Desktop development with C++ and C++ CMake tools for Windows.' }
$installations=(& $vswhere -latest -products '*' -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -format json | ConvertFrom-Json)
if (!$installations) { throw 'No Visual Studio C++ toolchain found. Add Desktop development with C++ in Visual Studio Installer.' }
$installation=$installations[0]
$major=([version]$installation.installationVersion).Major
$generator=switch ($major) { 18 {'Visual Studio 18 2026'} 17 {'Visual Studio 17 2022'} 16 {'Visual Studio 16 2019'} default {throw "Unsupported Visual Studio version $major; update the generator mapping."} }
$bundled=Join-Path $installation.installationPath 'Common7/IDE/CommonExtensions/Microsoft/CMake/CMake/bin/cmake.exe'
$cmake=if(Test-Path -LiteralPath $bundled){$bundled}else{(Get-Command cmake -ErrorAction Stop).Source}
$buildDirectory=Join-Path $PSScriptRoot 'build-vs'
$arguments=@('-S',$PSScriptRoot,'-B',$buildDirectory,'-G',$generator,'-A','x64',"-DCMAKE_GENERATOR_INSTANCE=$($installation.installationPath)")
if (!$PluginDirectory) { $PluginDirectory=Join-Path $PSScriptRoot "../Assets/Plugins/Flecs/x86_64" }
$arguments += "-DUNITY_PLUGIN_DIR=$([IO.Path]::GetFullPath($PluginDirectory))"
& $cmake @arguments
if ($LASTEXITCODE -ne 0) { throw 'Native solution generation failed. Check the CMake error above.' }
if ($Build) {
    & $cmake --build $buildDirectory --config $Configuration --parallel
    if ($LASTEXITCODE -ne 0) { throw 'Native build failed. Close any Unity Editor/player using the plugin before rebuilding.' }
}
$solution=Get-ChildItem -LiteralPath $buildDirectory -File | Where-Object Name -Match '^Ecaty\.slnx?$' | Select-Object -First 1
if (!$solution) { throw "Generated solution not found in $buildDirectory" }
Write-Output "Native solution: $($solution.FullName)"
