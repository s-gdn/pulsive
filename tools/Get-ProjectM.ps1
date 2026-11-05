$ErrorActionPreference='Stop'
$ProgressPreference='SilentlyContinue'
$projectRoot=Split-Path $PSScriptRoot -Parent
$cache=Join-Path $projectRoot '.tools/projectm'
$runtime=Join-Path $cache 'runtime'
if(Test-Path -LiteralPath (Join-Path $runtime 'ready-v1')) { return }
New-Item -ItemType Directory -Force $cache,$runtime | Out-Null
function Download-Verified($url,$file,$sha256) {
    if(!(Test-Path -LiteralPath $file)) { Invoke-WebRequest -UseBasicParsing $url -OutFile $file }
    if((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ne $sha256) { throw "Checksum mismatch: $file. Remove this cached file and retry." }
}
Download-Verified 'https://github.com/projectM-visualizer/frontend-sdl-cpp/releases/download/2.0.0-pre1/projectMSDL-2.0.0-win64.zip' (Join-Path $cache 'windows.zip') '7129CAE0757970BD4FB2BF96AA7D6153FE9872D414C88C82B04A6396BDFE235B'
Download-Verified 'https://github.com/projectM-visualizer/projectm/releases/download/v4.1.4/libprojectM-4.1.4.tar.gz' (Join-Path $cache 'source.tar.gz') '38CC45296BFE2DFC1C053C26C8E37BE1F8CB277F8ADBD541DFEA077A8ECDB3AE'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead((Join-Path $cache 'windows.zip'))
try {
    foreach($name in @('projectM-4.dll','glew32.dll')) {
        $entry=$zip.Entries | Where-Object FullName -eq "projectMSDL-2.0.0-win64/$name"
        if(!$entry) { throw "Missing runtime file: $name" }
        [IO.Compression.ZipFileExtensions]::ExtractToFile($entry,(Join-Path $runtime $name),$true)
    }
} finally { $zip.Dispose() }
$source=Join-Path $cache 'source'
New-Item -ItemType Directory -Force $source | Out-Null
& tar -xzf (Join-Path $cache 'source.tar.gz') -C $source
if($LASTEXITCODE -ne 0) { throw 'Could not unpack projectM source.' }
# Include the complete buildable source and its vendors; omit only documentation images.
& tar -czf (Join-Path $runtime 'projectm-source.tar.gz') '--exclude=libprojectM-4.1.4/docs' '--exclude=libprojectM-4.1.4/background.png' -C $source 'libprojectM-4.1.4'
if($LASTEXITCODE -ne 0) { throw 'Could not package projectM source.' }
Copy-Item -LiteralPath (Join-Path $source 'libprojectM-4.1.4/LICENSE.txt') -Destination (Join-Path $runtime 'PROJECTM-LICENSE.txt')
Download-Verified 'https://raw.githubusercontent.com/nigels-com/glew/glew-2.2.0/LICENSE.txt' (Join-Path $cache 'GLEW-LICENSE.txt') '8991CD11BEFEC7C6A63662700F03C0CC42E864B6E77C7E70B80297C951A7F0FF'
Copy-Item -LiteralPath (Join-Path $cache 'GLEW-LICENSE.txt') -Destination (Join-Path $runtime 'GLEW-LICENSE.txt')
@'
projectM 4.1.4 (LGPL-2.1-or-later), unmodified shared library.
Official Windows binary: projectM frontend-sdl-cpp release 2.0.0-pre1.
https://github.com/projectM-visualizer/frontend-sdl-cpp/releases/tag/2.0.0-pre1
Corresponding source and build instructions: projectm-source.tar.gz in this folder.
Upstream source: https://github.com/projectM-visualizer/projectm/releases/tag/v4.1.4
Only non-build documentation and the README background image were omitted from the source archive.
You may replace projectM-4.dll with an API-compatible version built from modified source.
GLEW: https://github.com/nigels-com/glew (see GLEW-LICENSE.txt).
The .milk files shipped with Pulsive are original Pulsive presets, not an upstream preset pack.
'@ | Set-Content -LiteralPath (Join-Path $runtime 'SOURCES.txt') -Encoding UTF8
Set-Content -LiteralPath (Join-Path $runtime 'ready-v1') -Value 'projectM 4.1.4'
