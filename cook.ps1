param(
)

$projectFolder = "$PSScriptRoot\Source\Unreal\401"
$projectName = "Unreal401"
$projectFile = Resolve-Path "$projectFolder\$projectName.uproject"
$destination = "$PSScriptRoot\Cooked\Unreal\401"
$editorPath = "C:\Program Files\Epic Games\UE_4.5\Engine\Binaries\Win64"

. "$editorPath\ue4editor-cmd" $projectFile -run=cook -targetplatform=WindowsNoEditor -stdout -nullrhi

Copy-Item -Path "$projectFolder\Saved\Cooked\WindowsNoEditor\$projectName\Content\*" -Recurse -Include "*.uasset" -Destination $Destination -Force
