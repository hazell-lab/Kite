# builds a ready to run copy of kite for windows.
# run it from powershell in the project folder.
# add -arch arm64 to build for an arm pc instead of a normal 64 bit pc.
param(
    [ValidateSet("x64", "arm64")]
    [string]$Arch = "x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$runtime = "win-$Arch"
$output = Join-Path $root "publish\$runtime"

# stop if any step fails
dotnet test (Join-Path $root "Kite.sln") -c Release
if ($LASTEXITCODE -ne 0) { throw "tests failed, nothing was published" }

dotnet publish (Join-Path $root "src\Kite.App\Kite.App.csproj") `
    -c Release `
    -r $runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -o $output
if ($LASTEXITCODE -ne 0) { throw "publish failed" }

$zip = Join-Path $root "publish\Kite-$runtime.zip"
if (Test-Path $zip) { Remove-Item $zip }
Compress-Archive -Path (Join-Path $output "*") -DestinationPath $zip

Write-Host ""
Write-Host "Done. Run $output\Kite.exe or share $zip"
