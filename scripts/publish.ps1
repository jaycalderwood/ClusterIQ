param(
    [string]$Configuration = "Release",
    [string]$Rid = "win-x64"
)

$ErrorActionPreference = "Stop"

Write-Host "Restoring packages..."
dotnet restore

Write-Host "Building..."
dotnet build -c $Configuration

Write-Host "Publishing portable output..."
dotnet publish .\HVTools\HVTools.csproj -c $Configuration -r $Rid --self-contained false -o .\artifacts\portable

Write-Host "Publishing self-contained output..."
dotnet publish .\HVTools\HVTools.csproj -c $Configuration -r $Rid --self-contained true -o .\artifacts\selfcontained

Write-Host "Creating ZIP package..."
if (Test-Path .\HVTools-portable-$Rid.zip) { Remove-Item .\HVTools-portable-$Rid.zip -Force }
Compress-Archive -Path .\artifacts\portable\* -DestinationPath .\HVTools-portable-$Rid.zip -Force

Write-Host "Done."
