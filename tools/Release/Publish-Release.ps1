param([string]$Version = $env:RELEASE_VERSION)

$ErrorActionPreference = 'Stop'
if ($Version -notmatch '^\d+\.\d+\.\d+$') { throw 'A stable release version is required.' }
if (-not $env:GH_TOKEN -or $env:GH_REPO -notmatch '^[\w.-]+/[\w.-]+$') {
    throw 'GitHub token and repository are required.'
}

function Invoke-Gh {
    $result = & gh @args
    if ($LASTEXITCODE -ne 0) { throw "GitHub command failed: $($args[0])" }
    return $result
}

$tag = "v$Version"
$installer = "publish/EndurancePilot-$Version-setup-win-x64/endurance-pilot-$Version-setup.exe"
$portable = "publish/EndurancePilot-$Version-compact-win-x64/endurance-pilot-$Version.exe"
foreach ($file in @($installer, $portable)) {
    if (-not (Test-Path -LiteralPath $file -PathType Leaf)) { throw "Missing release asset: $file" }
}
$checksums = "publish/EndurancePilot-$Version-setup-win-x64/SHA256SUMS.txt"
$lines = foreach ($file in @($installer, $portable)) {
    '{0}  {1}' -f (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant(), [IO.Path]::GetFileName($file)
}
[IO.File]::WriteAllLines((Join-Path (Get-Location) $checksums), $lines)

# Obtain the short-lived job credential only in this publish step; checkout does
# not leave a write token in .git/config for build tools or dependency scripts.
git -c credential.helper= -c 'credential.helper=!gh auth git-credential' push origin "refs/tags/$tag"
if ($LASTEXITCODE -ne 0) { throw 'Release tag push failed.' }

$headers = @{ Authorization = "Bearer $env:GH_TOKEN"; Accept = 'application/vnd.github+json' }
try {
    $release = Invoke-RestMethod -Uri "https://api.github.com/repos/$env:GH_REPO/releases/tags/$tag" -Headers $headers
} catch {
    if ([int]$_.Exception.Response.StatusCode -ne 404) { throw }
    $release = $null
}
if ($release -and -not $release.draft) {
    Write-Output "$tag is already published; leaving its assets unchanged."
    exit 0
}
if (-not $release) {
    Invoke-Gh release create $tag --verify-tag --draft --title "Endurance Pilot $Version" --generate-notes
}
Invoke-Gh release upload $tag $installer $portable $checksums --clobber
$makeLatest = 'true'
try {
    $latest = Invoke-RestMethod -Uri "https://api.github.com/repos/$env:GH_REPO/releases/latest" -Headers $headers
    if ($latest.tag_name -match '^v(\d+\.\d+\.\d+)$' -and [version]$Matches[1] -gt [version]$Version) {
        $makeLatest = 'false'
    }
} catch {
    if ([int]$_.Exception.Response.StatusCode -ne 404) { throw }
}
Invoke-Gh release edit $tag --draft=false "--latest=$makeLatest"
Write-Output "Published $tag with installer, portable executable and SHA-256 checksums."
