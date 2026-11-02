param([switch]$Portable,[switch]$Clean,[switch]$IncludeTests)
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
if ($Clean) { $Portable=$true }
$publishOutput=if($Clean) { 'dist-clean' } else { 'dist' }
if ($Portable) {
    if ($IncludeTests) { throw 'Development checks cannot be included in a distribution build. Use -IncludeTests without -Portable or -Clean.' }
    $buildOptions+=@('-p:DebugType=None','-p:DebugSymbols=false','-p:CopyDebugSymbolFilesFromPackages=false','-p:CopyOutputSymbolsToPublishDirectory=false','-p:ExcludeDistributionSymbols=true')
    if ($Clean) { $buildOptions+='-p:EnableCompressionInSingleFile=true' }
    & $dotnetExecutable publish AudioVisualiser.csproj -c Release -r win-x64 --self-contained true -o $publishOutput -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true @buildOptions
} else {
    & $dotnetExecutable build AudioVisualiser.csproj -c Release @buildOptions
}
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
if ($Portable) {
    Copy-Item -LiteralPath README.md -Destination (Join-Path $publishOutput 'README.md')
    Copy-Item -LiteralPath how-to.txt -Destination (Join-Path $publishOutput 'how-to.txt')
    Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot $publishOutput) -Filter '*.pdb' -File | Remove-Item
    $noticeFiles = @(
        '.tools/nuget/naudio/2.2.1/license.txt',
        '.tools/licenses/Win2D-LICENSE.txt',
        '.tools/licenses/VCRTForwarders-LICENSE.txt',
        '.tools/nuget/skiasharp/3.119.4/LICENSE.txt',
        '.tools/nuget/skiasharp.nativeassets.win32/3.119.4/LICENSE.txt',
        '.tools/nuget/skiasharp.nativeassets.win32/3.119.4/THIRD-PARTY-NOTICES.txt',
        '.tools/nuget/microsoft.netcore.app.runtime.win-x64/8.0.33/LICENSE.TXT',
        '.tools/nuget/microsoft.windowsdesktop.app.runtime.win-x64/8.0.33/LICENSE'
    )
    $licenseNotices = foreach ($noticeFile in $noticeFiles) {
        if (Test-Path -LiteralPath $noticeFile) { $noticeFile; Get-Content -LiteralPath $noticeFile -Raw }
    }
    $noticeOutput=Join-Path $publishOutput 'THIRD-PARTY-NOTICES.txt'
    $licenseNotices | Set-Content -LiteralPath $noticeOutput -Encoding UTF8
    Add-Content -LiteralPath $noticeOutput -Encoding UTF8 -Value "`nWindows Composition is provided by the operating system. Windows SDK .NET projections: Copyright Microsoft Corporation. Windows SDK licence terms: https://aka.ms/WinSDKLicenseURL . WinRT.Runtime is part of C#/WinRT: https://github.com/microsoft/CsWinRT/blob/master/LICENSE ."
}
