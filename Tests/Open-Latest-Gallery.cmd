@echo off
if not exist "%~dp0Results\LatestGallery.html" (echo Run the gallery tests first. & exit /b 1)
start "" "%~dp0Results\LatestGallery.html"
