@echo off
rem Pulls the latest from GitHub (unless you have local changes), builds and starts the dev game. Pass "editor" for the Godot editor.
rem The work lives in play_dev.ps1 so a pull that changes it can't break a running batch file.
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0play_dev.ps1" %1
