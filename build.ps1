[CmdletBinding()]
param (
    [Parameter()]
    $Version
)
$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $PSCommandPath

if (-not $Version) {
    $Version = (& git -C $here rev-parse --short HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Unable to determine the current git SHA.' }
}

# Additional tag from the branch name. Container tags can't contain '/', so e.g. feature/x becomes feature-x.
# A detached HEAD (common in CI) reports 'HEAD', which isn't a useful tag, so it is skipped.
$branch = (& git -C $here rev-parse --abbrev-ref HEAD).Trim()
$tags = @($Version)
if ($LASTEXITCODE -eq 0 -and $branch -and $branch -ne 'HEAD') {
    $tags += ($branch -replace '[^A-Za-z0-9_.-]', '-')
}

Write-Host "Publishing container with tags: $($tags -join ', ')"
# ContainerImageTags takes a semicolon-separated list; the quotes stop PowerShell splitting on ';'.
& dotnet publish $here/src/FamilyTree.Web -t:PublishContainer "-p:ContainerImageTags=`"$($tags -join ';')`""
if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE." }
