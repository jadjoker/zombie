# Gets the latest code from GitHub, builds it and starts the dev game (or the editor with "editor").
# The update is skipped, and your local version runs, when you have uncommitted edits, unpushed commits,
# are on another branch, or are offline. So local changes are never touched.
param([string]$Mode = "")

$godot = "C:\Users\smitt\Projects\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe"
$root = Resolve-Path "$PSScriptRoot\..\.."
$project = Join-Path $root "godot"

function Update-FromGitHub {
	Set-Location $root
	$branch = git rev-parse --abbrev-ref HEAD
	if ($branch -ne "main") { Write-Host "On branch '$branch', not updating." -ForegroundColor Yellow; return $true }

	Write-Host "Checking GitHub for updates..."
	git fetch --quiet origin main 2>$null
	if ($LASTEXITCODE -ne 0) { Write-Host "Couldn't reach GitHub, running your current version." -ForegroundColor Yellow; return $true }

	$behind = [int](git rev-list --count HEAD..origin/main)
	$ahead = [int](git rev-list --count origin/main..HEAD)
	$dirty = git status --porcelain
	if ($behind -eq 0) { Write-Host "Already up to date."; return $true }

	if ($dirty -or $ahead -gt 0) {
		Write-Host ""
		Write-Host "GitHub has $behind new commit(s), but you have local changes, so NOT updating." -ForegroundColor Yellow
		Write-Host "Running your local version. Commit and pull (or push) when you're ready to merge." -ForegroundColor Yellow
		Start-Sleep -Seconds 4
		return $true
	}

	git merge --ff-only --quiet origin/main
	if ($LASTEXITCODE -ne 0) { Write-Host "Update failed, running your current version." -ForegroundColor Yellow; return $true }
	Write-Host "Updated: pulled $behind new commit(s)." -ForegroundColor Green
	git log --oneline -n ([Math]::Min($behind, 5)) | ForEach-Object { Write-Host "  $_" }
	return $true
}

Update-FromGitHub | Out-Null

Write-Host "Building..."
dotnet build "$project\RiskOfCopyright.csproj" -nologo -v q
if ($LASTEXITCODE -ne 0) {
	Write-Host ""
	Write-Host "Build failed. See the errors above." -ForegroundColor Red
	Read-Host "Press Enter to close"
	exit 1
}

if ($Mode -eq "editor") { Start-Process $godot -ArgumentList "--path", "`"$project`"", "--editor" }
else { Start-Process $godot -ArgumentList "--path", "`"$project`"" }
