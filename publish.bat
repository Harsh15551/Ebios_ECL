@echo off
echo ========================================================
echo  Publishing ECL eBios Portal for Production Deployment
echo ========================================================
echo.

echo [1/3] Cleaning previous publish folder...
if exist publish rmdir /s /q publish

echo [2/3] Building and publishing Release package...
dotnet publish src\Ebios.Web\Ebios.Web.csproj -c Release -o .\publish

if %ERRORLEVEL% equ 0 (
    echo [3/3] Copying environment template...
    copy .env.example publish\.env.example
    echo.
    echo ========================================================
    echo  SUCCESS: Application published to folder: .\publish\
    echo  Copy the contents of .\publish\ to your server!
    echo ========================================================
) else (
    echo.
    echo [ERROR] Publish failed. Please inspect build errors above.
)
pause

