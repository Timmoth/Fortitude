Both the package and the image are published by `.github/workflows/release.yml` on a
`v*` tag — these commands are only for publishing by hand.

```bash
# Manually build / push the fortitude image
docker login ghcr.io -u <github-username>   # needs a token with write:packages
docker buildx build --platform linux/amd64,linux/arm64 \
  -t ghcr.io/timmoth/fortitude:v10.1.4 \
  -t ghcr.io/timmoth/fortitude:latest --push .

# Build / Pack fortitude client nuget package
cd Fortitude.Client
dotnet build -c Release
dotnet pack -c Release
```
