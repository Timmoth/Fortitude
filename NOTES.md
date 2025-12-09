```bash
# Manually build / push fortitude-server
docker build -t fortitude-server .
docker tag fortitude-server aptacode/fortitude-server:v10.0.5
docker tag fortitude-server aptacode/fortitude-server:latest
docker push aptacode/fortitude-server:v10.0.5             
docker push aptacode/fortitude-server:latest        

# Build / Pack fortitude client nuget package
cd Fortitude.Client 
dotnet build -c Release  
dotnet pack -c Release
```