FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
# Restore first (cached layer): only build props and project files.
COPY Directory.Build.props Directory.Packages.props ./
COPY src/Bank.Domain/Bank.Domain.csproj src/Bank.Domain/
COPY src/Bank.Application/Bank.Application.csproj src/Bank.Application/
COPY src/Bank.Infrastructure/Bank.Infrastructure.csproj src/Bank.Infrastructure/
COPY src/Bank.Api/Bank.Api.csproj src/Bank.Api/
RUN dotnet restore src/Bank.Api/Bank.Api.csproj
COPY src/ src/
RUN dotnet publish src/Bank.Api/Bank.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# Volume mount point for KYC documents, writable by the non-root user.
RUN mkdir -p /app/storage && chown app /app/storage
USER app
EXPOSE 8080
ENTRYPOINT ["dotnet", "Bank.Api.dll"]
