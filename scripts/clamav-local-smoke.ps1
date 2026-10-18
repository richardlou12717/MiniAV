param(
    [Parameter(Mandatory=$true)][string]$ClamScanExe,
    [string]$DatabaseDirectory
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$exe = [System.IO.Path]::GetFullPath($ClamScanExe)
if (-not [System.IO.Path]::IsPathFullyQualified($ClamScanExe) -or
    [System.IO.Path]::GetFileName($exe) -ine 'clamscan.exe' -or
    -not (Test-Path -LiteralPath $exe -PathType Leaf)) {
    throw 'Provide an existing, absolute path to clamscan.exe.'
}
if ($DatabaseDirectory) {
    if (-not [System.IO.Path]::IsPathFullyQualified($DatabaseDirectory) -or
        -not (Test-Path -LiteralPath $DatabaseDirectory -PathType Container)) {
        throw 'DatabaseDirectory must be an existing absolute directory.'
    }
}
Push-Location $root
try {
    dotnet build .\src\MiniAV.Cli\MiniAV.Cli.csproj -c Release
    if ($LASTEXITCODE -ne 0) { throw 'CLI build failed' }

    $scratch = Join-Path ([System.IO.Path]::GetTempPath()) ('miniav-clam-smoke-' + [guid]::NewGuid().ToString('N'))
    New-Item -ItemType Directory -Path $scratch | Out-Null
    try {
        $ordinary = Join-Path $scratch 'harmless sample with spaces.txt'
        [System.IO.File]::WriteAllText($ordinary, 'MiniAV harmless integration test; not a malware sample.')
        $stdout = Join-Path $scratch 'stdout.txt'
        $stderr = Join-Path $scratch 'stderr.txt'
        $args = @(
            'run','--no-build','--configuration','Release',
            '--project','.\src\MiniAV.Cli','--',
            '--path',('"' + $ordinary + '"'),
            '--engine','clamav','--clamav-exe',('"' + $exe + '"')
        )
        if ($DatabaseDirectory) {
            $args += @('--clamav-db', ('"' + [System.IO.Path]::GetFullPath($DatabaseDirectory) + '"'))
        }
        $child = Start-Process -FilePath 'dotnet' -ArgumentList $args -Wait -NoNewWindow -PassThru -RedirectStandardOutput $stdout -RedirectStandardError $stderr
        $text = (Get-Content -LiteralPath $stdout -Raw -ErrorAction SilentlyContinue) +
                (Get-Content -LiteralPath $stderr -Raw -ErrorAction SilentlyContinue)
        Write-Host "MiniAV exit code: $($child.ExitCode)"
        Write-Host $text
        if ($child.ExitCode -ne 0 -or $text -notmatch '(?m)^NO_MATCH ' -or
            $text -notmatch 'SUMMARY Matched=0 NoMatch=1 Skipped=0 Errors=0') {
            throw 'Real ClamAV smoke test was not clean/completed. Inspect output, database and flags; do not merge.'
        }
        Write-Host 'Real ClamAV single-file smoke test passed (no proof of complete malware coverage).'
    }
    finally { Remove-Item -LiteralPath $scratch -Recurse -Force }
}
finally { Pop-Location }
