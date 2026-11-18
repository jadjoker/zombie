@echo off
rem Run once: puts a "Risk of Copyright" shortcut on the desktop that pulls the latest game from GitHub and starts it.
set TARGET=%~dp0play_latest.bat
set ICON=C:\Users\smitt\Projects\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe
powershell -NoProfile -Command "$d=[Environment]::GetFolderPath('Desktop'); $s=(New-Object -ComObject WScript.Shell).CreateShortcut(\"$d\Risk of Copyright.lnk\"); $s.TargetPath='%TARGET%'; $s.WorkingDirectory='%~dp0'; $s.IconLocation='%ICON%,0'; $s.Description='Pull the latest Risk of Copyright from GitHub and play'; $s.Save()"
if errorlevel 1 (
	echo Could not make the shortcut.
) else (
	echo Made "Risk of Copyright" on your desktop.
)
pause
