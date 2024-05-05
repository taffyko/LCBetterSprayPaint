Set-Location "$PSScriptRoot\.."
dotnet build
if (!$?) { exit 1 }
[xml]$csproj = Get-Content .\*.csproj
$name = $csproj.Project.PropertyGroup.AssemblyName
Remove-Item "..\..\BepInEx\plugins\$name.dll" -ErrorAction SilentlyContinue
$null = mkdir -Force "..\..\BepInEx\scripts"
netcode-patch ".\bin\Debug\netstandard2.1\$name.dll" "..\..\Lethal Company_Data\Managed"
Copy-Item ".\bin\Debug\netstandard2.1\$name.dll" "..\..\BepInEx\scripts\$name.dll"
$cfg = "..\..\BepInEx\config\BepInEx.cfg"
(Get-Content $cfg) -replace "^HideManagerGameObject = false","HideManagerGameObject = true" | Set-Content $cfg