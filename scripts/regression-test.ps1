$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Push-Location $root
try {
    dotnet build .\src\MiniAV.Cli\MiniAV.Cli.csproj --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Build failed' }
    dotnet run --project .\tests\MiniAV.Core.Regression\MiniAV.Core.Regression.csproj --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'Core cancellation regression tests failed' }

    dotnet run --project .\tests\MiniAV.ClamAV.Regression\MiniAV.ClamAV.Regression.csproj --configuration Release
    if ($LASTEXITCODE -ne 0) { throw 'ClamAV adapter regression tests failed' }

    $temp = Join-Path ([System.IO.Path]::GetTempPath()) ('miniav-test-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $temp | Out-Null
    try {
        # Start-Process separates stderr from PowerShell's terminating-error pipeline.
        # Nonzero exit codes are expected for matches (1) and bad input (2).
        function Invoke-Scan {
            param(
                [string]$Target,
                [string]$Database,
                [int]$ExpectedExitCode,
                [string]$ExpectedText,
                [string]$ForbiddenText = ''
            )
            $stdout = Join-Path $temp 'stdout.txt'
            $stderr = Join-Path $temp 'stderr.txt'
            $arguments = @(
                'run', '--no-build', '--configuration', 'Release',
                '--project', '.\src\MiniAV.Cli',
                '--', '--path', ('"' + $Target + '"'),
                '--db', ('"' + $Database + '"')
            )
            $process = Start-Process -FilePath 'dotnet' -ArgumentList $arguments -Wait -PassThru -NoNewWindow -RedirectStandardOutput $stdout -RedirectStandardError $stderr
            $output = (Get-Content $stdout -Raw -ErrorAction SilentlyContinue) + (Get-Content $stderr -Raw -ErrorAction SilentlyContinue)
            if ($process.ExitCode -ne $ExpectedExitCode -or $output -notmatch $ExpectedText -or ($ForbiddenText -and $output -match $ForbiddenText)) {
                throw "Scan failed: expected exit $ExpectedExitCode and '$ExpectedText'; actual exit $($process.ExitCode): $output"
            }
        }

        $db = Join-Path $root 'signatures\demo-hashes.json'
        Invoke-Scan -Target (Join-Path $root 'tests\fixtures') -Database $db -ExpectedExitCode 1 -ExpectedText 'MATCHED'
        $ordinary = Join-Path $temp 'ordinary.txt'
        [System.IO.File]::WriteAllText($ordinary, 'ordinary file')
        Invoke-Scan -Target $ordinary -Database $db -ExpectedExitCode 0 -ExpectedText 'NO_MATCH'
        Invoke-Scan -Target (Join-Path $temp 'missing') -Database $db -ExpectedExitCode 2 -ExpectedText 'Target does not exist'
        $bad = Join-Path $temp 'invalid.json'
        [System.IO.File]::WriteAllText($bad, '{"version":"bad","signatures":[{"sha256":"xyz"}]}')
        Invoke-Scan -Target $ordinary -Database $bad -ExpectedExitCode 2 -ExpectedText 'invalid SHA-256'
        # Match both fixture line-ending variants independent of checkout settings.
        $fixtureText = "MiniAV demonstration fixture. This file is harmless."
        $lfFile = Join-Path $temp 'lf.txt'
        $crlfFile = Join-Path $temp 'crlf.txt'
        [System.IO.File]::WriteAllBytes($lfFile, [System.Text.Encoding]::ASCII.GetBytes($fixtureText + "`n"))
        [System.IO.File]::WriteAllBytes($crlfFile, [System.Text.Encoding]::ASCII.GetBytes($fixtureText + "`r`n"))
        Invoke-Scan -Target $lfFile -Database $db -ExpectedExitCode 1 -ExpectedText 'MATCHED \[DEMO-0001: DemoOnly\]'
        Invoke-Scan -Target $crlfFile -Database $db -ExpectedExitCode 1 -ExpectedText 'MATCHED \[DEMO-0001-WIN: DemoOnly\]'

        # A junction must be reported as SKIPPED, never traversed to outside content.
        $outside = Join-Path $temp 'outside'
        $inside = Join-Path $temp 'scan-root'
        New-Item -ItemType Directory -Path $outside, $inside | Out-Null
        [System.IO.File]::Copy($lfFile, (Join-Path $outside 'outside-match.txt'))
        $junction = Join-Path $inside 'linked-outside'
        New-Item -ItemType Junction -Path $junction -Target $outside -ErrorAction Stop | Out-Null
        Invoke-Scan -Target $inside -Database $db -ExpectedExitCode 0 -ExpectedText 'SUMMARY Matched=0 NoMatch=0 Skipped=1 Errors=0' -ForbiddenText '(?m)^(?:MATCHED|NO_MATCH|ERROR)\s'
        Invoke-Scan -Target $junction -Database $db -ExpectedExitCode 0 -ExpectedText 'SKIPPED \(directory reparse point\)' -ForbiddenText '(?m)^(?:MATCHED|NO_MATCH|ERROR)\s'

        Write-Host 'Regression tests passed'
    }
    finally { Remove-Item $temp -Recurse -Force }
}
finally { Pop-Location }
