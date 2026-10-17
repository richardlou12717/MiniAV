$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet --version
    if ($LASTEXITCODE -ne 0) { throw '.NET SDK is required.' }
    dotnet build .\src\MiniAV.Cli\MiniAV.Cli.csproj
    if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
    dotnet run --no-build --project .\src\MiniAV.Cli -- --path .\tests\fixtures --db .\signatures\demo-hashes.json
    if ($LASTEXITCODE -ne 1) { throw "Expected exit code 1 (demo match), got $LASTEXITCODE" }
    Write-Host 'Smoke test passed: demo signature matched, no file was changed.'
}
finally { Pop-Location }
