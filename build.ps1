[CmdletBinding()]
param (
    [Parameter()]
    $Version = 'local'
)
$here = Split-Path -Parent $PSCommandPath
& dotnet publish $here/src/FamilyTree.Web -t:PublishContainer -p:ContainerImageTags=$Version