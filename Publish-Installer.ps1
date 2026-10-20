param(
    [string]$OutputDirectory,
    [string]$CompilerPath
)
$ErrorActionPreference = 'Stop'
[xml]$project = Get-Content -LiteralPath (Join-Path $PSScriptRoot 'EndurancePilot.App/EndurancePilot.App.csproj') -Raw
$version = [string]$project.SelectSingleNode('/Project/PropertyGroup/Version').InnerText
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Installer requires a stable three-part version.' }
if (-not $OutputDirectory) { $OutputDirectory = Join-Path $PSScriptRoot "publish/EndurancePilot-$version-setup-win-x64" }
$OutputDirectory = [IO.Path]::GetFullPath($OutputDirectory)
if (-not $CompilerPath) {
    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    $candidates = @(
        $(if ($command) { $command.Source }),
        (Join-Path $PSScriptRoot 'artifacts/build-tools/InnoSetup/ISCC.exe'),
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 7\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )
    $CompilerPath = $candidates | Where-Object { $_ -and (Test-Path -LiteralPath $_) } | Select-Object -First 1
}
if (-not $CompilerPath -or -not (Test-Path -LiteralPath $CompilerPath)) {
    throw 'Install Inno Setup 6.7 or newer from https://jrsoftware.org/isdl.php, or pass -CompilerPath.'
}
$payloadDirectory = Join-Path $PSScriptRoot "publish/EndurancePilot-$version-compact-win-x64"
& (Join-Path $PSScriptRoot 'Publish-SingleExe.ps1') -OutputDirectory $payloadDirectory
$payload = Join-Path $payloadDirectory "endurance-pilot-$version.exe"
New-Item -ItemType Directory -Path $OutputDirectory -Force | Out-Null
& $CompilerPath "/DAppVersion=$version" "/DPayload=$payload" "/DOutputPath=$OutputDirectory" (Join-Path $PSScriptRoot 'installer/EndurancePilot.iss')
if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed.' }
$installer = Join-Path $OutputDirectory "endurance-pilot-$version-setup.exe"
if (-not (Test-Path -LiteralPath $installer)) { throw 'Installer output missing.' }
Get-Item -LiteralPath $installer | Select-Object FullName,Length
Get-FileHash -LiteralPath $installer -Algorithm SHA256
