# ============================================
# 1. Build stage
# ============================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy everything (solution + all projects)
COPY . .

# Restore from solution file
RUN dotnet restore Fortitude.sln

# Publish the server project
RUN dotnet publish Fortitude.Server/Fortitude.Server.csproj -c Release -o /app/publish

# ============================================
# 2. Runtime stage
# ============================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080

COPY --from=build /app/publish .

EXPOSE 8080

ENTRYPOINT ["dotnet", "Fortitude.Server.dll"]
