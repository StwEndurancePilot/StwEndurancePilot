param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
[xml]$appProject = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'EndurancePilot.App/EndurancePilot.App.csproj') -Raw
$releaseVersion = [string]$appProject.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if ($releaseVersion -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'A valid release version is required for the output filename.' }
$releaseFileName = "endurance-pilot-$releaseVersion.exe"
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot "publish/EndurancePilot-$releaseVersion-compact-win-x64" }
Push-Location $PSScriptRoot
try {
    dotnet restore EndurancePilot.App -r win-x64 -p:SelfContained=false -p:PublishSelfContained=false
    if ($LASTEXITCODE -ne 0) { throw 'Restore failed.' }
    $assets = Get-Content -LiteralPath 'EndurancePilot.App/obj/project.assets.json' -Raw | ConvertFrom-Json
    $nativePackage = @($assets.libraries.PSObject.Properties | Where-Object { $_.Name -like 'OpenCvSharp4.runtime.win.slim/*' })
    if ($nativePackage.Count -ne 1) { throw 'Cannot identify the native recognition package.' }
    $nativeSource = $null
    foreach ($folder in $assets.packageFolders.PSObject.Properties.Name) {
        $candidate = Join-Path $folder ($nativePackage[0].Value.path + '/runtimes/win-x64/native/OpenCvSharpExtern.dll')
        if (Test-Path -LiteralPath $candidate) { $nativeSource = $candidate; break }
    }
    if (-not $nativeSource) { throw 'Native recognition library is missing.' }
    $nativeHash = (Get-FileHash -LiteralPath $nativeSource -Algorithm SHA256).Hash
    $nativeDirectory = Join-Path $PSScriptRoot ('artifacts/compact-native/brotli-smallest-' + $nativeHash)
    New-Item -ItemType Directory -Path $nativeDirectory -Force | Out-Null
    $packedNative = Join-Path $nativeDirectory 'OpenCvSharpExtern.dll.br'
    if (-not (Test-Path -LiteralPath $packedNative)) {
        $inputStream = [IO.File]::OpenRead($nativeSource)
        try {
            $outputStream = [IO.File]::Create($packedNative + '.tmp')
            try {
                $compressor = [IO.Compression.BrotliStream]::new($outputStream, [IO.Compression.CompressionLevel]::SmallestSize)
                try { $inputStream.CopyTo($compressor) } finally { $compressor.Dispose() }
            } finally { $outputStream.Dispose() }
        } finally { $inputStream.Dispose() }
        Move-Item -LiteralPath ($packedNative + '.tmp') -Destination $packedNative
    }
    [IO.File]::WriteAllText($packedNative + '.sha256', $nativeHash)

    dotnet publish EndurancePilot.App -c Release -r win-x64 --self-contained false `
        -p:SelfContained=false -p:PublishSelfContained=false `
        "-p:CompactNativeDirectory=$nativeDirectory" `
        -p:PublishSingleFile=true -p:IncludeAllContentForSelfExtract=true `
        -p:EnableCompressionInSingleFile=false -p:DebugType=None -p:DebugSymbols=false `
        -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Single EXE publish failed.' }

    $outputFiles = @(Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File)
    if (@($outputFiles | Where-Object { $_.Name -notin @('EndurancePilot.App.exe', $releaseFileName) -or $_.DirectoryName -ne (Get-Item -LiteralPath $OutputDirectory).FullName }).Count -gt 0) {
        throw 'Unexpected files in the output folder. Use a fresh output folder.'
    }
    $releaseExe = Join-Path $OutputDirectory $releaseFileName
    Move-Item -LiteralPath (Join-Path $OutputDirectory 'EndurancePilot.App.exe') -Destination $releaseExe -Force
    $outputFiles = @(Get-ChildItem -LiteralPath $OutputDirectory -Recurse -File)
    if ($outputFiles.Count -ne 1 -or $outputFiles[0].Name -ne $releaseFileName) { throw 'Expected exactly one versioned release EXE.' }


    Write-Output "Single EXE ready: $($outputFiles[0].FullName)"
    Write-Output 'Requires .NET 10 Windows Desktop Runtime (x64).'
}
finally { Pop-Location }
