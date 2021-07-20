param(
    [string]$ProjectName = 'Blank_4_5_1',
    [string]$Destination = 'D:\repos\dotnetparser\TestAssets\Unreal\401'
)

$editorPath = "C:\Program Files\Epic Games\UE_4.5\Engine\Binaries\Win64"

. "$editorPath\ue4editor-cmd" "$PSScriptRoot\$ProjectName\$ProjectName.uproject" -run=cook -targetplatform=WindowsNoEditor -stdout -nullrhi

Copy-Item -Path "$PSScriptRoot\$ProjectName\Saved\Cooked\WindowsNoEditor\$ProjectName\Content\*" -Include "*.uasset" -Destination $Destination -Force
