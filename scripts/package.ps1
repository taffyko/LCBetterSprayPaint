$ErrorActionPreference = 'Stop'
Set-Location "$PSScriptRoot\.."
dotnet build -c Release

$info = (Get-Content manifest.json | ConvertFrom-Json)
$version = $info.version_number
$name = $info.name

if (Test-Path BepInEx) { Remove-Item -Recurse BepInEx }
mkdir -Force BepInEx\plugins
Copy-Item "bin/Release/netstandard2.1/$name.dll" "BepInEx/plugins"

Compress-Archive -Force -Path @(
	"manifest.json",
	"icon.png",
	"README.md",
	"CHANGELOG.md",
	"BepInEx"
) -DestinationPath "taffyko-$name-$version.zip"

Remove-Item -Recurse BepInEx