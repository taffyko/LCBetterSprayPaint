Set-Location "$PSScriptRoot\.."
dotnet build -c Release
if ($?) {
	[xml]$csproj = Get-Content .\*.csproj
	$name = $csproj.Project.PropertyGroup.AssemblyName
	netcode-patch ".\bin\Release\netstandard2.1\$name.dll" "..\..\Lethal Company_Data\Managed"
	Copy-Item ".\bin\Release\netstandard2.1\$name.dll" "..\..\BepInEx\plugins"
	Copy-Item ".\bin\Release\netstandard2.1\$name.pdb" "..\..\BepInEx\plugins"
	Remove-Item "..\..\BepInEx\scripts\$name.dll" -ErrorAction SilentlyContinue
	Remove-Item "..\..\BepInEx\scripts\$name.pdb" -ErrorAction SilentlyContinue
	$cfg = "..\..\BepInEx\config\BepInEx.cfg"
	(Get-Content $cfg) -replace "^HideManagerGameObject = true","HideManagerGameObject = false" | Set-Content $cfg
}