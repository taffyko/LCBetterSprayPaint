Set-Location "$PSScriptRoot\.."
dotnet build -c Release
if ($?) {
	$name = (Get-Content manifest.json | ConvertFrom-Json).name
	Copy-Item ".\bin\Release\netstandard2.1\$name.dll" "..\..\BepInEx\plugins"
	$cfg = "..\..\BepInEx\config\BepInEx.cfg"
	(Get-Content $cfg) -replace "^HideManagerGameObject = true","HideManagerGameObject = false" | Set-Content $cfg
}