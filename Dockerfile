# Multi-stage .NET 8 Dockerfile for AcxiomCRM

# Stage 1: Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project specification and nuget config for efficient layer caching
COPY ["AcxiomCRM.csproj", "./"]
COPY ["nuget.config", "./"]
RUN dotnet restore "AcxiomCRM.csproj"

# Copy remaining source code and publish in Release configuration
COPY . .
RUN dotnet publish "AcxiomCRM.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Stage 2: ASP.NET Core Runtime
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Create directories for SQLite database and ASP.NET Core Data Protection keys
RUN mkdir -p /app/data /app/.keys

# Copy compiled artifacts from build stage
COPY --from=build /app/publish .

# Configure container networking to listen on 0.0.0.0:8080
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENV PORT=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "AcxiomCRM.dll"]
