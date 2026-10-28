@echo off
setlocal
for /f "usebackq tokens=1,* delims==" %%A in ("%~dp0project_env.config.txt") do set "%%A=%%B"
pushd "%CS_PACKAGE_ENGINE%"
call "%CS_PACKAGE_ENGINE%\Engine\Build\BatchFiles\Build.bat" -Target="%CS_PACKAGE_EDITOR% %CS_PACKAGE_PLATFORM% %CS_PACKAGE_CONFIGURATION% -Project=%CS_PACKAGE_PROJECT%" -Target="ShaderCompileWorker Win64 Development -Project=%CS_PACKAGE_PROJECT% -Quiet" -architecture=%CS_PACKAGE_ARCHITECTURE% -WaitMutex -FromMsBuild
set "CS_PACKAGE_RESULT=%ERRORLEVEL%"
popd
exit /b %CS_PACKAGE_RESULT%
