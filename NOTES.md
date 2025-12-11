```bash
# Manually build / push fortitude-server
docker buildx build --platform linux/amd64,linux/arm64 -t aptacode/fortitude-server:v10.0.8 -t aptacode/fortitude-server:latest --push .    

# Build / Pack fortitude client nuget package
cd Fortitude.Client 
dotnet build -c Release  
dotnet pack -c Release
```