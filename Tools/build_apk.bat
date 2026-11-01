@echo off
setlocal
if "%ANDROID_SDK_ROOT%"=="" set ANDROID_SDK_ROOT=E:/android-sdk
if "%JAVA_HOME%"=="" set JAVA_HOME=C:/Program Files/Microsoft/jdk-17.0.19.10-hotspot
python "%~dp0build_apk.py" %*
endlocal
