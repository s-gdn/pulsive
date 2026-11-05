param([switch]$Portable,[switch]$Clean,[switch]$Exe,[switch]$IncludeTests)
$ErrorActionPreference = 'Stop'
Set-Location $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $PSScriptRoot '.tools/cli'
$env:NUGET_PACKAGES = Join-Path $PSScriptRoot '.tools/nuget'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:MSBUILDDISABLENODEREUSE = '1'
$env:DOTNET_CLI_USE_MSBUILD_SERVER = '0'
$dotnetExecutable = if (Test-Path '.tools/dotnet/dotnet.exe') { Join-Path $PSScriptRoot '.tools/dotnet/dotnet.exe' } else { 'dotnet' }
if ($IncludeTests -and !(Test-Path -LiteralPath 'Testing/TestLauncher.cs')) { throw 'Local development checks are not available in Testing/.' }
$buildOptions = @('--disable-build-servers','-p:UseSharedCompilation=false',"-p:IncludeDevelopmentTests=$($IncludeTests.ToString().ToLowerInvariant())")
& (Join-Path $PSScriptRoot 'tools/Get-ProjectM.ps1')
function Write-DistributionNotices($noticeOutput) {
    $noticeFiles = @(
        '.tools/nuget/naudio/2.2.1/license.txt',
        '.tools/licenses/Win2D-LICENSE.txt',
        '.tools/licenses/VCRTForwarders-LICENSE.txt',
        '.tools/projectm/runtime/PROJECTM-LICENSE.txt',
        '.tools/projectm/runtime/GLEW-LICENSE.txt',
        '.tools/nuget/skiasharp/3.119.4/LICENSE.txt',
        '.tools/nuget/skiasharp.nativeassets.win32/3.119.4/LICENSE.txt',
        '.tools/nuget/skiasharp.nativeassets.win32/3.119.4/THIRD-PARTY-NOTICES.txt',
        '.tools/nuget/microsoft.netcore.app.runtime.win-x64/8.0.33/LICENSE.TXT',
        '.tools/nuget/microsoft.windowsdesktop.app.runtime.win-x64/8.0.33/LICENSE'
    )
    $licenseNotices = foreach ($noticeFile in $noticeFiles) {
        if (Test-Path -LiteralPath $noticeFile) { $noticeFile; Get-Content -LiteralPath $noticeFile -Raw }
    }
    $licenseNotices | Set-Content -LiteralPath $noticeOutput -Encoding UTF8
    Add-Content -LiteralPath $noticeOutput -Encoding UTF8 -Value "`nWindows Composition is provided by the operating system. Windows SDK .NET projections: Copyright Microsoft Corporation. Windows SDK licence terms: https://aka.ms/WinSDKLicenseURL . WinRT.Runtime is part of C#/WinRT: https://github.com/microsoft/CsWinRT/blob/master/LICENSE ."
}
if ($Clean -and $Exe) { throw 'Use either -Clean or -Exe to choose an output folder.' }
if ($Clean -or $Exe) { $Portable=$true }
if ($Exe) {
    # Restore first so licence files are available even on a fresh checkout.
    & $dotnetExecutable restore AudioVisualiser.csproj -r win-x64 --disable-build-servers -p:UseSharedCompilation=false -p:SingleExeDistribution=true
    if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
    New-Item -ItemType Directory -Force -Path '.tools/licenses' | Out-Null
    Write-DistributionNotices (Join-Path $PSScriptRoot '.tools/licenses/Pulsive-THIRD-PARTY-NOTICES.txt')
}
$publishOutput=if($Exe) { 'dist-exe' } elseif($Clean) { 'dist-clean' } else { 'dist' }
if ($Portable) {
    if ($IncludeTests) { throw 'Development checks cannot be included in a distribution build. Use -IncludeTests without -Portable, -Clean or -Exe.' }
    $buildOptions+=@('-p:DebugType=None','-p:DebugSymbols=false','-p:CopyDebugSymbolFilesFromPackages=false','-p:CopyOutputSymbolsToPublishDirectory=false','-p:ExcludeDistributionSymbols=true')
    if ($Clean -or $Exe) { $buildOptions+='-p:EnableCompressionInSingleFile=true' }
    if ($Exe) { $buildOptions+='-p:SingleExeDistribution=true' }
    & $dotnetExecutable publish AudioVisualiser.csproj -c Release -r win-x64 --self-contained true -o $publishOutput -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true @buildOptions
} else {
    & $dotnetExecutable build AudioVisualiser.csproj -c Release @buildOptions
}
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Portable -and !$Exe) {
    Copy-Item -LiteralPath README.md -Destination (Join-Path $publishOutput 'README.md')
    Copy-Item -LiteralPath how-to.txt -Destination (Join-Path $publishOutput 'how-to.txt')
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $publishOutput) -Filter '*.pdb' -File | Remove-Item
    Write-DistributionNotices (Join-Path $publishOutput 'THIRD-PARTY-NOTICES.txt')
}
