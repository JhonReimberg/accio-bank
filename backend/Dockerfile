FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/Bank.Api/Bank.Api.csproj src/Bank.Api/
RUN dotnet restore src/Bank.Api/Bank.Api.csproj
COPY src/ src/
RUN dotnet publish src/Bank.Api/Bank.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bank.Api.dll"]
