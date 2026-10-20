param(
    [string]$SourceSha = $env:RELEASE_SOURCE_SHA,
    [string]$RunId = $env:RELEASE_RUN_ID
)

$ErrorActionPreference = 'Stop'
if ($SourceSha -notmatch '^[0-9a-f]{40}$' -or $RunId -notmatch '^\d+$') {
    throw 'A source commit SHA and workflow run ID are required.'
}

function Invoke-Git {
    $result = & git @args
    if ($LASTEXITCODE -ne 0) { throw "Git command failed: $($args[0])" }
    return $result
}

Invoke-Git fetch origin --tags
$head = Invoke-Git rev-parse HEAD
if ($head -ne $SourceSha) { throw 'Checkout does not match the triggering push.' }
if (Invoke-Git status --porcelain --untracked-files=no) { throw 'Tracked files must be clean.' }

$projectPath = 'EndurancePilot.App/EndurancePilot.App.csproj'
[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$baseline = [string]$project.Project.PropertyGroup.Version
if ($baseline -notmatch '^\d+\.\d+\.\d+$') { throw 'Project version must have three numeric parts.' }
$highest = [version]$baseline
$existingTag = $null
foreach ($tag in @(Invoke-Git tag --list 'v*')) {
    if ($tag -notmatch '^v(\d+\.\d+\.\d+)$') { continue }
    $version = [version]$Matches[1]
    if ($version -gt $highest) { $highest = $version }
    $message = (Invoke-Git log -1 --format=%B $tag) -join "`n"
    if ($message -match "(?m)^Release-Run: $RunId$") {
        if ($message -notmatch "(?m)^Release-Source: $SourceSha$") {
            throw 'Existing release run belongs to a different source commit.'
        }
        if ($existingTag) { throw 'Multiple release tags belong to this run.' }
        $existingTag = $tag
    }
}

if ($existingTag) {
    Invoke-Git checkout --detach $existingTag
    $releaseVersion = $existingTag.Substring(1)
    [xml]$releasedProject = Get-Content -LiteralPath $projectPath -Raw
    if ([string]$releasedProject.Project.PropertyGroup.Version -ne $releaseVersion) {
        throw 'Existing tag and project version disagree.'
    }
} else {
    $releaseVersion = '{0}.{1}.{2}' -f $highest.Major, $highest.Minor, ($highest.Build + 1)
    if ($highest.Major -gt 65535 -or $highest.Minor -gt 65535 -or $highest.Build -ge 65535) {
        throw 'Version exceeds the Windows installer version range. Increase the minor version explicitly.'
    }
    $path = Join-Path (Get-Location) $projectPath
    $text = [IO.File]::ReadAllText($path)
    if ([regex]::Matches($text, '<Version>[^<]+</Version>').Count -ne 1) {
        throw 'Expected exactly one project Version element.'
    }
    $text = [regex]::Replace($text, '<Version>[^<]+</Version>', "<Version>$releaseVersion</Version>")
    [IO.File]::WriteAllText($path, $text)
    Invoke-Git config user.name 'github-actions[bot]'
    Invoke-Git config user.email '41898282+github-actions[bot]@users.noreply.github.com'
    Invoke-Git add -- $projectPath
    Invoke-Git commit -m "Release v$releaseVersion" -m "Release-Run: $RunId`nRelease-Source: $SourceSha"
    Invoke-Git tag "v$releaseVersion"
}

if ($env:GITHUB_OUTPUT) {
    "version=$releaseVersion" >> $env:GITHUB_OUTPUT
}
Write-Output "Prepared v$releaseVersion from $SourceSha"
