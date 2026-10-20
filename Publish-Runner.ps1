param([string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'EndurancePilot.App/EndurancePilot.App.csproj') -Raw
$version = [string]$project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot "publish/EndurancePilot-$version-win-x64" }
Push-Location $PSScriptRoot
try {
    dotnet run --project EndurancePilot.Checks -c Release
    if ($LASTEXITCODE -ne 0) { throw 'Core checks failed; publish cancelled.' }
    dotnet publish EndurancePilot.App -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o $OutputDirectory
    if ($LASTEXITCODE -ne 0) { throw 'Publish failed.' }
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'QUICKSTART.txt') -Destination (Join-Path $OutputDirectory 'QUICKSTART.txt')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'README.md') -Destination (Join-Path $OutputDirectory 'README.md')
    $checkDirectory = Join-Path $PSScriptRoot 'artifacts\package-checks'
    $probe = Start-Process -FilePath (Join-Path $OutputDirectory 'EndurancePilot.App.exe') -ArgumentList @('--self-check', ('"' + $checkDirectory + '"')) -WindowStyle Hidden -Wait -PassThru
    if ($probe.ExitCode -ne 0) { throw "Packaged startup checks failed. Inspect $checkDirectory" }
    Write-Output "Runner package ready: $OutputDirectory"
}
finally { Pop-Location }
