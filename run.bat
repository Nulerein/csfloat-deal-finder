@echo off
cd /d "%~dp0"
dotnet run --project "CSFloatDealFinder.csproj"
if errorlevel 1 pause
