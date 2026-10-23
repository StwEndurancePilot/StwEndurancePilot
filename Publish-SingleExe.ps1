param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
[xml]$appProject = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'EndurancePilot.App/EndurancePilot.App.csproj') -Raw
$releaseVersion = [string]$appProject.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if ($releaseVersion -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?$') { throw 'A valid release version is required for the output filename.' }
$releaseFileName = "endurance-pilot-$releaseVersion.exe"
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot "publish/EndurancePilot-$releaseVersion-compact-win-x64" }
Push-Location $PSScriptRoot
try {
    dotnet run --project EndurancePilot.Checks -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core checks failed; publish cancelled.' }

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

    
    $checkRoot = Join-Path $PSScriptRoot ('artifacts\single-exe-checks\' + [Guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $checkRoot -Force | Out-Null
    $isolatedExe = Join-Path $checkRoot $releaseFileName
    Copy-Item -LiteralPath $outputFiles[0].FullName -Destination $isolatedExe
    $resultDirectory = Join-Path $checkRoot 'results'
    $extractionRoot = Join-Path $checkRoot 'extracted'
    $oldExtractionRoot = $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR
    try {
        $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $extractionRoot
        $probe = Start-Process -FilePath $isolatedExe -WorkingDirectory $checkRoot `
            -ArgumentList @('--self-check', ('"' + $resultDirectory + '"')) -WindowStyle Hidden -Wait -PassThru
    } finally { $env:DOTNET_BUNDLE_EXTRACT_BASE_DIR = $oldExtractionRoot }
    if ($probe.ExitCode -ne 0) { throw "Single EXE startup checks failed. Inspect $resultDirectory" }
    $checks = @(Get-Content -LiteralPath (Join-Path $resultDirectory 'startup-checks.json') -Raw | ConvertFrom-Json)
    if ($checks.Count -eq 0 -or @($checks | Where-Object { -not $_.Passed }).Count -gt 0) {
        throw "Single EXE checks did not pass. Inspect $resultDirectory"
    }
    $runtimeConfigs = @(Get-ChildItem -LiteralPath $extractionRoot -Recurse -Filter 'EndurancePilot.App.runtimeconfig.json')
    if ($runtimeConfigs.Count -ne 1) { throw 'Cannot verify the bundled runtime configuration.' }
    $runtime = Get-Content -LiteralPath $runtimeConfigs[0].FullName -Raw | ConvertFrom-Json
    if ($runtime.runtimeOptions.includedFrameworks -or -not ($runtime.runtimeOptions.frameworks.name -contains 'Microsoft.WindowsDesktop.App')) {
        throw 'The EXE must use the installed Windows Desktop runtime, not bundle its own.'
    }
    $bundleDirectory = $runtimeConfigs[0].DirectoryName
    $allowedFiles = @(
        'EndurancePilot.App.dll','EndurancePilot.Core.dll','EndurancePilot.App.deps.json','EndurancePilot.App.runtimeconfig.json',
        'Microsoft.Windows.SDK.NET.dll','WinRT.Runtime.dll','OpenCvSharp.dll','OpenCvSharp.WpfExtensions.dll',
        'Wpf.Ui.dll','Wpf.Ui.Abstractions.dll','BouncyCastle.Cryptography.dll','OpenCvSharpExtern.dll.br','OpenCvSharpExtern.dll.br.sha256','OpenCvSharpExtern.dll'
    )
    $allowedFiles += @(Get-ChildItem -LiteralPath 'EndurancePilot.App/assets' -Filter '*.png' | ForEach-Object { 'assets/' + $_.Name })
    $inventory = @(Get-ChildItem -LiteralPath $bundleDirectory -Recurse -File | ForEach-Object {
        [pscustomobject]@{ File = [IO.Path]::GetRelativePath($bundleDirectory, $_.FullName).Replace('\','/'); Bytes = $_.Length }
    })
    if (@($inventory | Where-Object { $_.File -notin $allowedFiles }).Count -gt 0) {
        throw 'The bundle contains unexpected files. Inspect the isolated extraction before sharing.'
    }
    foreach ($assembly in @('EndurancePilot.App.dll','EndurancePilot.Core.dll')) {
        $bytes = [IO.File]::ReadAllBytes((Join-Path $bundleDirectory $assembly))
        $ascii = [Text.Encoding]::UTF8.GetString($bytes)
        $unicode = [Text.Encoding]::Unicode.GetString($bytes)
        if ($ascii.Contains('MPDB') -or $ascii.Contains($env:USERPROFILE) -or $unicode.Contains($env:USERPROFILE) -or
            $ascii.Contains($env:USERNAME) -or $unicode.Contains($env:USERNAME)) {
            throw 'Debug data or a local user path was found in an application assembly.'
        }
    }
    $inventory | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $resultDirectory 'bundle-inventory.json')
    Write-Output 'Bundle audit passed: installed runtime, expected files only, no embedded app debug data or local user paths.'
    Write-Output "$($checks.Count) isolated single EXE checks passed."
    Write-Output "Single EXE ready: $($outputFiles[0].FullName)"
    Write-Output 'Requires .NET 10 Windows Desktop Runtime (x64).'
}
finally { Pop-Location }
