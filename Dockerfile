# ============================================
# 1. Build stage
# ============================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy everything (solution + all projects)
COPY . .

# Restore the server and the client it references, rather than the whole solution:
# the Examples and the multi-targeted client test project contribute nothing to the
# image and a good deal to the build time.
RUN dotnet restore Fortitude.Server/Fortitude.Server.csproj

# Publish the server project
RUN dotnet publish Fortitude.Server/Fortitude.Server.csproj -c Release --no-restore -o /app/publish

# ============================================
# 2. Runtime stage
# ============================================
FROM mcr.microsoft.com/dotnet/aspnet:11.0 AS runtime
WORKDIR /app

COPY --from=build /app/publish .

# Handler definitions are read from, and written back to, /app/config. It is created
# and handed to the app user here so the live UI and admin API can save files on a
# default `docker run`.
#
# Note for bind mounts: a host directory mounted over /app/config keeps the host's
# ownership. Mount it writable by UID $APP_UID (or run with --user) if you intend to
# edit handlers through the UI.
RUN mkdir -p /app/config && chown -R $APP_UID /app

# Not root. The server binds whatever PORT/PORTS asks for, so stay above 1024.
USER $APP_UID

ENTRYPOINT ["dotnet", "Fortitude.Server.dll"]
