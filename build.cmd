@echo off
rem Builds b0r3d QR with the .NET SDK. The program runs on .NET Framework 4.8, which comes with
rem Windows 10 and 11, so running it needs nothing installed; only building it needs the SDK.
rem Run "build.cmd test" to build and run the tests too.
rem Double-clicked, the window stays open at the end so the result can be read.
rem Keep this file plain ASCII with CRLF line endings, or cmd misreads it.
setlocal
cd /d "%~dp0"
call :build %1
set "RC=%ERRORLEVEL%"
if "%~1"=="" echo %cmdcmdline% | "%WINDIR%\System32\find.exe" /i "%~nx0" >nul && pause
exit /b %RC%

:build
where dotnet >nul 2>nul
if errorlevel 1 (
    echo Can't find the .NET SDK ^(dotnet^). Building b0r3d QR needs it: https://dotnet.microsoft.com/download
    exit /b 1
)

rem A running b0r3dQR.exe can't be replaced: say so plainly instead of a build error.
if exist bin\b0r3dQR.exe (
    2>nul (>>bin\b0r3dQR.exe call ) || (
        echo b0r3d QR is running from bin\b0r3dQR.exe. Close it first.
        exit /b 1
    )
)

rem The build stamp: the commit GitHub builds from (its first 7 characters), or "dev" for a
rem build made anywhere else. The exe's Product version says "0.1 build abc1234".
set "BUILD=dev"
if defined B0R3DQR_BUILD set "BUILD=%B0R3DQR_BUILD:~0,7%"

rem -nodeReuse and UseSharedCompilation off: no SDK helper processes are left running afterwards
dotnet build tests\b0r3dQRTests.csproj -c Release -nologo -v quiet -nodeReuse:false -p:UseSharedCompilation=false -p:BuildStamp=%BUILD%
call :check "The build" || exit /b 1

echo Built bin\b0r3dQR.exe, build %BUILD%
if /i not "%~1"=="test" exit /b 0
rem The tests, in three parts that each finish within 90 seconds (their watchdog), even on a
rem 2-core CI machine: the exe and making codes, then files and reading codes, then the window.
bin\tests\b0r3dQRTests.exe --part make
call :check "The make tests" || exit /b 1
bin\tests\b0r3dQRTests.exe --part scan
call :check "The scan tests" || exit /b 1
bin\tests\b0r3dQRTests.exe --part window
call :check "The window tests" || exit /b 1
exit /b 0

rem Any exit code but 0 is a failure. "if errorlevel 1" alone is only true from 1 up, so it
rem misses a negative code, such as a program that crashes as it closes (0xC000000D is -1073741811).
:check
if errorlevel 1 goto failed
if not errorlevel 0 goto failed
exit /b 0
:failed
echo %~1 ended with exit code %ERRORLEVEL%.
exit /b 1
