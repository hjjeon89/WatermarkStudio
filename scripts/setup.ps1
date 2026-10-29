param([ValidateSet('Build', 'Install')][string]$Action = 'Build')

$ErrorActionPreference = 'Stop'
$previousInputEncoding = [Console]::InputEncoding
$previousOutputEncoding = [Console]::OutputEncoding
$previousPipelineEncoding = $OutputEncoding
$utf8 = New-Object System.Text.UTF8Encoding($false)
[Console]::InputEncoding = $utf8
[Console]::OutputEncoding = $utf8
$OutputEncoding = $utf8
$projectRoot = Split-Path -Parent $PSScriptRoot

function Get-RuntimeId {
    $nativeArchitecture = $env:PROCESSOR_ARCHITEW6432
    if (-not $nativeArchitecture) { $nativeArchitecture = $env:PROCESSOR_ARCHITECTURE }
    switch ($nativeArchitecture) {
        'AMD64' { return 'win-x64' }
        'ARM64' { return 'win-arm64' }
        'x86' { return 'win-x86' }
        default { throw "Unsupported Windows architecture: $nativeArchitecture" }
    }
}

function Test-DotNetSdk([string]$Executable) {
    try {
        $versions = & $Executable --list-sdks 2>$null
        return ($LASTEXITCODE -eq 0 -and [bool]($versions -match '^10\.0\.\d+\s+\['))
    }
    catch { return $false }
}

function Get-DotNetSdk {
    $localSdk = Join-Path $env:LOCALAPPDATA 'WatermarkStudio\dotnet'
    $localDotnet = Join-Path $localSdk 'dotnet.exe'
    $command = Get-Command dotnet -CommandType Application -ErrorAction SilentlyContinue
    $candidates = @()
    if ($command) { $candidates += $command.Source }
    if (Test-Path -LiteralPath $localDotnet -PathType Leaf) { $candidates += $localDotnet }
    foreach ($candidate in $candidates) {
        if (Test-DotNetSdk $candidate) { return $candidate }
    }

    Write-Host '.NET 10 SDK was not found. Installing the stable SDK for this user...'
    Write-Host 'Official source: https://dot.net/v1/dotnet-install.ps1'
    $installer = Join-Path ([IO.Path]::GetTempPath()) ('watermark-dotnet-' + [guid]::NewGuid().ToString('N') + '.ps1')
    try {
        [Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
        Invoke-WebRequest -UseBasicParsing -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $installer
        $architecture = (Get-RuntimeId).Substring(4)
        & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File $installer -Channel 10.0 -Quality GA -Architecture $architecture -InstallDir $localSdk -NoPath | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'The official .NET SDK installer failed. Check your Internet connection and retry.' }
        if (-not (Test-DotNetSdk $localDotnet)) { throw '.NET 10 SDK installation could not be verified.' }
        return $localDotnet
    }
    finally { if (Test-Path -LiteralPath $installer) { Remove-Item -LiteralPath $installer -Force } }
}

function Assert-PublishDirectory([string]$Directory) {
    foreach ($name in @('WatermarkStudio.exe', 'WatermarkStudio.runtimeconfig.json', 'coreclr.dll', 'PresentationFramework.dll')) {
        if (-not (Test-Path -LiteralPath (Join-Path $Directory $name) -PathType Leaf)) {
            throw "Incomplete self-contained application: missing $name in $Directory. Run build.bat again."
        }
    }
}

function Build-App {
    $project = Join-Path $projectRoot 'src\WatermarkStudio\WatermarkStudio.csproj'
    if (-not (Test-Path -LiteralPath $project -PathType Leaf)) { throw 'Source project not found. This is a built package; run install.bat instead.' }
    $dotnet = Get-DotNetSdk
    $rid = Get-RuntimeId
    $publish = Join-Path $projectRoot "dist\$rid"
    $output = (Join-Path $projectRoot ".artifacts\test-check\build\$rid").Replace('\', '/') + '/'
    Write-Host "Building Watermark Studio ($rid, Release, runtime included)..."
    Push-Location -LiteralPath $projectRoot
    try {
        & $dotnet publish $project --configuration Release --runtime $rid --self-contained true --output $publish "-p:OutDir=$output" --nologo | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Build failed. Review the errors above; installation has not started.' }
    }
    finally { Pop-Location }
    Assert-PublishDirectory $publish
    New-Item -ItemType Directory -Path (Join-Path $publish 'scripts') -Force | Out-Null
    Copy-Item -LiteralPath (Join-Path $projectRoot 'install.bat') -Destination $publish -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'scripts\setup.ps1') -Destination (Join-Path $publish 'scripts\setup.ps1') -Force
    Copy-Item -LiteralPath (Join-Path $projectRoot 'README.md') -Destination $publish -Force
    Write-Host "Build complete: $publish"
    Write-Host 'Distribute this entire folder. The receiving PC does not need .NET or an SDK.'
    return $publish
}

function Install-App([string]$PublishDirectory, [string]$InstallDirectory) {
    Assert-PublishDirectory $PublishDirectory
    $source = [IO.Path]::GetFullPath($PublishDirectory).TrimEnd('\')
    $destination = [IO.Path]::GetFullPath($InstallDirectory).TrimEnd('\')
    $executable = Join-Path $destination 'WatermarkStudio.exe'
    $running = Get-Process -Name WatermarkStudio -ErrorAction SilentlyContinue | Where-Object { $_.Path -eq $executable }
    if ($running) { throw 'Watermark Studio is running from the installation folder. Close it and run install.bat again.' }
    if ($source -ne $destination) {
        New-Item -ItemType Directory -Path $destination -Force | Out-Null
        Get-ChildItem -LiteralPath $source | Where-Object { $_.Name -ne 'WatermarkStudio.exe' } |
            Copy-Item -Destination $destination -Recurse -Force
        Copy-Item -LiteralPath (Join-Path $source 'WatermarkStudio.exe') -Destination $executable -Force
    }
    Write-Host "Installed: $executable"
    return $executable
}

function New-AppShortcut([string]$ShortcutPath, [string]$Executable) {
    $shell = New-Object -ComObject WScript.Shell
    $shortcut = $shell.CreateShortcut($ShortcutPath)
    $shortcut.TargetPath = $Executable
    $shortcut.WorkingDirectory = Split-Path -Parent $Executable
    $shortcut.IconLocation = "$Executable,0"
    $shortcut.Description = 'Watermark Studio - image watermark editor'
    $shortcut.Save()
}

if ($MyInvocation.InvocationName -ne '.') {
    $exitCode = 0
    try {
        if ($Action -eq 'Build') { Build-App | Out-Null }
        else {
            if (Test-Path -LiteralPath (Join-Path $projectRoot 'WatermarkStudio.exe') -PathType Leaf) { $payload = $projectRoot }
            else { $payload = Build-App }
            $installation = Join-Path $env:LOCALAPPDATA 'Programs\WatermarkStudio'
            $executable = Install-App -PublishDirectory $payload -InstallDirectory $installation
            foreach ($folder in @([Environment]::GetFolderPath('DesktopDirectory'), [Environment]::GetFolderPath('Programs'))) {
                if ($folder) { New-AppShortcut -ShortcutPath (Join-Path $folder 'Watermark Studio.lnk') -Executable $executable }
            }
            Write-Host 'Installation complete. Open Watermark Studio from the desktop or Start menu.'
        }
    }
    catch { Write-Host "ERROR: $($_.Exception.Message)" -ForegroundColor Red; $exitCode = 1 }
    finally {
        [Console]::InputEncoding = $previousInputEncoding
        [Console]::OutputEncoding = $previousOutputEncoding
        $OutputEncoding = $previousPipelineEncoding
    }
    exit $exitCode
}
