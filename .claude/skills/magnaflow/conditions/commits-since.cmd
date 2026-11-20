@echo off
rem commits-since <hours> [prefix...] - a MagnaFlow action condition.
rem Exit 0 when HEAD has a commit from the last <hours> hours whose subject starts with none of
rem the prefixes (literal, case-sensitive), 1 when it has none, 2 on bad usage or a git failure.
rem One stdout line, e.g. "3 commits in the last 16h (2 ignored)".
rem No labels or goto: the kit's zip may carry this file with LF line endings, which breaks
rem label lookups in cmd.exe. One findstr per prefix: findstr with several case-sensitive
rem literals of different lengths can miss matches. A prefix must not contain * or ?.
setlocal EnableExtensions DisableDelayedExpansion

set "hours=%~1"
set "bad="
if "%hours%"=="" set "bad=1"
for /f "delims=0123456789" %%x in ("%hours%") do set "bad=1"
set "nonzero="
for /f "tokens=* delims=0" %%x in ("%hours%") do set "nonzero=%%x"
if not defined nonzero set "bad=1"
if defined bad (
    echo usage: commits-since ^<hours^> [prefix...] ^(hours: a positive integer^)
    exit /b 2
)

set "list=%TEMP%\commits-since-%RANDOM%%RANDOM%.txt"
git log --since="%hours% hours ago" --format=%%s HEAD > "%list%"
if errorlevel 1 (
    del "%list%" >nul 2>&1
    echo commits-since: git log failed
    exit /b 2
)
set /a total=0
for /f %%n in ('type "%list%" ^| find /c /v ""') do set /a total=%%n

set "first=1"
for %%p in (%*) do (
    if defined first (
        set "first="
    ) else (
        findstr /b /l /v /c:"%%~p" "%list%" > "%list%.next"
        move /y "%list%.next" "%list%" >nul
    )
)

set /a kept=0
for /f %%n in ('type "%list%" ^| find /c /v ""') do set /a kept=%%n
del "%list%" >nul 2>&1
set /a ignored=total-kept
if %kept%==0 (
    echo no commits in the last %hours%h ^(%ignored% ignored^)
    exit /b 1
)
set "noun=commits"
if %kept%==1 set "noun=commit"
echo %kept% %noun% in the last %hours%h ^(%ignored% ignored^)
exit /b 0
