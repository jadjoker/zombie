@echo off
rem Pulls the newest game from GitHub (branch main), builds it and starts it. make_desktop_shortcut.bat puts this on the desktop.
title Risk of Copyright
rem Runs from a copy in TEMP, since the pull can rewrite this file while it runs.
if not "%~1"=="--copy" (
	copy /y "%~f0" "%TEMP%\roc_play_latest.bat" >nul
	"%TEMP%\roc_play_latest.bat" --copy "%~dp0"
	exit /b
)
set TOOLS=%~2
cd /d "%TOOLS%..\.."
set GODOT_CONSOLE=C:\Users\smitt\Projects\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64_console.exe

echo Getting the latest changes from GitHub...
git fetch origin main
if errorlevel 1 (
	echo Could not reach GitHub. Starting the copy you already have.
	goto play
)
for /f "delims=" %%b in ('git rev-parse --abbrev-ref HEAD') do set BRANCH=%%b
git diff --quiet
if errorlevel 1 (
	echo You have unsaved local edits, so nothing was pulled. Starting your copy on %BRANCH%.
	goto play
)
if /i not "%BRANCH%"=="main" git checkout main
git merge --ff-only origin/main
if errorlevel 1 echo Could not update main cleanly. Starting the copy you already have.

:play
echo Importing any new assets...
"%GODOT_CONSOLE%" --headless --path "%cd%\godot" --import >nul 2>&1
call "%TOOLS%play_dev.bat"
