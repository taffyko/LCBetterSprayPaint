param (
	[Switch] $Release = $false,
	[bool] $HideManagerGameObject = !$Release
)
Set-Location "$PSScriptRoot\.."
[xml]$csproj = Get-Content .\*.csproj
$name = $csproj.Project.PropertyGroup.AssemblyName

if ($Release) {
	dotnet build -c Release
	if (!$?) { exit 1 }
	netcode-patch -uv 2022.3.62 -nv 1.12.0 -tv 1.0.0 ".\bin\Release\netstandard2.1\$name.dll" "..\..\Lethal Company_Data\Managed"
	Copy-Item ".\bin\Release\netstandard2.1\$name.dll" "..\..\BepInEx\plugins\"
	Copy-Item ".\bin\Release\netstandard2.1\$name.pdb" "..\..\BepInEx\plugins\"
	Remove-Item "..\..\BepInEx\scripts\$name*.dll" -ErrorAction SilentlyContinue
	Remove-Item "..\..\BepInEx\scripts\$name*.pdb" -ErrorAction SilentlyContinue
} else {
	# NOTE: During development, I use a custom build of ScriptEngine to hot-reload DLLs placed in BepInEx\scripts
	dotnet build
	if (!$?) { exit 1 }
	$null = mkdir -Force "..\..\BepInEx\scripts"
	# Copy immediately so that non-NGO modules can be hot-reloaded right away
	Copy-Item ".\bin\Debug\netstandard2.1\$name.dll" "..\..\BepInEx\scripts\"
	Copy-Item ".\bin\Debug\netstandard2.1\$name.pdb" "..\..\BepInEx\scripts\"
	netcode-patch -uv 2022.3.62 -nv 1.12.0 -tv 1.0.0 ".\bin\Debug\netstandard2.1\$name.dll" "..\..\Lethal Company_Data\Managed"
	Remove-Item "..\..\BepInEx\plugins\$name.dll" -ErrorAction SilentlyContinue
	Remove-Item "..\..\BepInEx\plugins\$name.pdb" -ErrorAction SilentlyContinue
	# Then copy the fully-patched binaries when ready (NGO modules can only be reloaded after leaving the lobby anyway)
	Copy-Item ".\bin\Debug\netstandard2.1\$name.dll" "..\..\BepInEx\scripts\"
	Copy-Item ".\bin\Debug\netstandard2.1\$name.pdb" "..\..\BepInEx\scripts\"
}

$cfg = "..\..\BepInEx\config\BepInEx.cfg"
if ($HideManagerGameObject -eq $true) {
	(Get-Content $cfg) -replace "^HideManagerGameObject = false","HideManagerGameObject = true" | Set-Content $cfg
} elseif ($HideManagerGameObject -eq $false) {
	(Get-Content $cfg) -replace "^HideManagerGameObject = true","HideManagerGameObject = false" | Set-Content $cfg
}
