<#
    KaraokeForge - one-time GitHub setup for your own PC
    ------------------------------------------------------
    Run this in PowerShell from the folder that contains this script
    (the one with Program.cs). It syncs everything to your GitHub
    repository and activates the automatic Windows build.

        powershell -ExecutionPolicy Bypass -File .\setup-git.ps1

    It needs git installed (https://git-scm.com) and a GitHub account
    with access to the repository.
#>

$ErrorActionPreference = "Stop"

$repoUrl = "https://github.com/Narco995/KaraokeForge.git"
$workflow = ".github\workflows\build-windows.yml"

Write-Host ""
Write-Host "KaraokeForge - GitHub setup" -ForegroundColor Cyan
Write-Host "----------------------------"

if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
    Write-Host "Git is not installed. Install it first: winget install Git.Git" -ForegroundColor Yellow
    exit 1
}

if (-not (Test-Path ".\Program.cs")) {
    Write-Host "Run this script from the folder that contains Program.cs." -ForegroundColor Yellow
    exit 1
}

# 1. clone (or pull) the repository
if (Test-Path ".\.git") {
    Write-Host "Updating the existing clone..."
    git -C . pull --ff-only
} else {
    Write-Host "Cloning $repoUrl ..."
    git clone $repoUrl .\karaokeforge-repo
    Copy-Item -Recurse -Force -Exclude ".git" * .\karaokeforge-repo\
    Set-Location .\karaokeforge-repo
}

# 2. make sure the GitHub Actions workflow is in place
New-Item -ItemType Directory -Force -Path ".github\workflows" | Out-Null
Copy-Item -Force ".\$workflow" ".github\workflows\build-windows.yml" -ErrorAction SilentlyContinue

# 3. commit and push everything
git add -A
git commit -m "KaraokeForge: sync the project and enable the Windows build" 2>$null
git push origin main

Write-Host ""
Write-Host "Done. Open this page to watch the build:" -ForegroundColor Green
Write-Host "  https://github.com/Narco995/KaraokeForge/actions"
Write-Host ""
Write-Host "When it turns green, download the exe from:"
Write-Host "  Actions -> Build Windows app -> Artifacts -> KaraokeForge-windows-x64"
Write-Host ""
