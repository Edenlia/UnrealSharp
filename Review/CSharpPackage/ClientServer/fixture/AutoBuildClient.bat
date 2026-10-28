@echo off
setlocal
for /f "usebackq tokens=1,* delims==" %%A in ("%~dp0project_env.config.txt") do set "%%A=%%B"
pushd "%CS_PACKAGE_ENGINE%"
call "%CS_PACKAGE_ENGINE%\Engine\Build\BatchFiles\Build.bat" %CS_PACKAGE_CLIENT% %CS_PACKAGE_PLATFORM% %CS_PACKAGE_CONFIGURATION% -Project="%CS_PACKAGE_PROJECT%" -architecture=%CS_PACKAGE_ARCHITECTURE% -WaitMutex -FromMsBuild
set "CS_PACKAGE_RESULT=%ERRORLEVEL%"
popd
exit /b %CS_PACKAGE_RESULT%
