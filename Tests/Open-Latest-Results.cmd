@echo off
if not exist "%~dp0Results\Latest.html" (echo Run tests first. & exit /b 1)
start "" "%~dp0Results\Latest.html"
