Set-Location "$PSScriptRoot\.."
dotnet build
if ($?) {
	$name = (Get-Content manifest.json | ConvertFrom-Json).name
	Remove-Item "..\..\BepInEx\plugins\$name.dll" -ErrorAction SilentlyContinue
	Copy-Item ".\bin\Debug\netstandard2.1\$name.dll" "..\..\BepInEx\scripts"
	$cfg = "..\..\BepInEx\config\BepInEx.cfg"
	(Get-Content $cfg) -replace "^HideManagerGameObject = false","HideManagerGameObject = true" | Set-Content $cfg
}