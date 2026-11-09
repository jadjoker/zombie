@echo off
rem Builds the current code and starts the dev version of the game (pass "editor" to open the Godot editor instead).
set GODOT=C:\Users\smitt\Projects\godot\Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe
set PROJECT=%~dp0..\..\godot
echo Building...
dotnet build "%PROJECT%\RiskOfCopyright.csproj" -nologo -v q
if errorlevel 1 (
	echo.
	echo Build failed. See the errors above.
	pause
	exit /b 1
)
if /i "%1"=="editor" (
	start "" "%GODOT%" --path "%PROJECT%" --editor
) else (
	start "" "%GODOT%" --path "%PROJECT%"
)
