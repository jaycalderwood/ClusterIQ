param(
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [string]$Version = "1.0.1"
)

$ErrorActionPreference = "Stop"

dotnet restore .\HVTools\HVTools.csproj
dotnet build .\HVTools\HVTools.csproj -c $Configuration --no-restore `
  /p:Version=$Version `
  /p:AssemblyVersion="$Version.0" `
  /p:FileVersion="$Version.0" `
  /p:InformationalVersion=$Version

dotnet publish .\HVTools\HVTools.csproj -c $Configuration -r $Runtime --self-contained true -o .\artifacts\publish `
  /p:PublishSingleFile=true `
  /p:IncludeNativeLibrariesForSelfExtract=true `
  /p:PublishTrimmed=false `
  /p:Version=$Version `
  /p:AssemblyVersion="$Version.0" `
  /p:FileVersion="$Version.0" `
  /p:InformationalVersion=$Version

$packageRoot = ".\artifacts\package\ClusterIQ"
New-Item -ItemType Directory -Force -Path $packageRoot | Out-Null
Copy-Item .\artifacts\publish\* $packageRoot -Recurse -Force

if (Test-Path .\HVTools\Assets\ClusterIQ.ico) {
    Copy-Item .\HVTools\Assets\ClusterIQ.ico $packageRoot -Force
}

$exe = Get-ChildItem .\artifacts\publish -Filter *.exe | Select-Object -First 1
if (-not $exe) { throw "No EXE found in publish output." }

$exeTarget = ".\artifacts\ClusterIQ_${Version}_win-x64.exe"
Copy-Item $exe.FullName $exeTarget -Force

$zipTarget = ".\artifacts\ClusterIQ_${Version}_win-x64.zip"
if (Test-Path $zipTarget) { Remove-Item $zipTarget -Force }
Compress-Archive -Path "$packageRoot\*" -DestinationPath $zipTarget -Force

Get-FileHash $exeTarget -Algorithm SHA256 | Format-List
Get-FileHash $zipTarget -Algorithm SHA256 | Format-List

Write-Host "Created:"
Write-Host " - $exeTarget"
Write-Host " - $zipTarget"
