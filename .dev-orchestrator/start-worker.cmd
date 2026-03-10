@echo off
title NSFinance Worker
cd /d "C:\Users\MariusAlbu\Desktop\Projects\NSFinance\apps\worker\src\NSFinance.Worker"
set ASPNETCORE_ENVIRONMENT=Development
set NSFINANCE_ALLOW_REMOTE_DB_IN_DEVELOPMENT=false
set NSFINANCE_DB_CONNECTION_STRING=
set NSFINTECH_DB_CONNECTION_STRING=
set ConnectionStrings__DefaultConnection=
dotnet run