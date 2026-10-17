$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet build .\src\MiniAV.Cli\MiniAV.Cli.csproj --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ('miniav-test-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp | Out-Null
    try {
        $fixtures = Join-Path $temp 'fixtures'
        New-Item -ItemType Directory -Path $fixtures | Out-Null
        $demo = Join-Path $fixtures 'demo-sample.txt'
        $ordinary = Join-Path $fixtures 'ordinary-sample.txt'
        [System.IO.File]::WriteAllBytes($demo, [System.Text.Encoding]::UTF8.GetBytes("This is a harmless MiniAV demo test file.`n"))
        [System.IO.File]::WriteAllBytes($ordinary, [System.Text.Encoding]::UTF8.GetBytes("ordinary file"))
        # Use the repository fixtures for match verification, including checked-in line endings.
        $output = & dotnet run --no-build --configuration Release --project .\src\MiniAV.Cli -- --path .\tests\fixtures --db .\signatures\demo-hashes.json 2>&1
        if ($LASTEXITCODE -ne 1 -or !($output -match 'MATCHED') -or !($output -match 'NO_MATCH')) {
            throw "Fixture scan failed: $output"
        }
        $output = & dotnet run --no-build --configuration Release --project .\src\MiniAV.Cli -- --path $ordinary --db .\signatures\demo-hashes.json 2>&1
        if ($LASTEXITCODE -ne 0 -or !($output -match 'NO_MATCH')) { throw "Negative scan failed: $output" }
        $output = & dotnet run --no-build --configuration Release --project .\src\MiniAV.Cli -- --path (Join-Path $temp 'missing') --db .\signatures\demo-hashes.json 2>&1
        if ($LASTEXITCODE -ne 2) { throw "Missing target should exit 2: $output" }
        $bad = Join-Path $temp 'invalid.json'
        [System.IO.File]::WriteAllText($bad, '{"version":"bad","signatures":[{"sha256":"xyz"}]}')
        $output = & dotnet run --no-build --configuration Release --project .\src\MiniAV.Cli -- --path $ordinary --db $bad 2>&1
        if ($LASTEXITCODE -ne 2) { throw "Invalid DB should exit 2: $output" }
        Write-Host 'Regression tests passed'
    }
    finally { Remove-Item $temp -Recurse -Force }
}
finally { Pop-Location }
