# Installation

Fortitude ships two things, both published from GitHub:

| Artefact | Where | Used for |
|---|---|---|
| `Fortitude` NuGet package | [GitHub Packages](https://github.com/Timmoth?tab=packages&repo_name=Fortitude) | The library your tests reference. |
| `ghcr.io/timmoth/fortitude` image | [GHCR](https://github.com/Timmoth/Fortitude/pkgs/container/fortitude) | The standalone mock server. |

Neither is on nuget.org. The older `aptacode/fortitude-server` Docker Hub image still carries
the last hand-pushed build but will not be updated again.

---

## The package

### Why a token is needed

GitHub Packages requires authentication for NuGet **even when the package is public**. This is
a platform limitation, not a setting on this repository — there is no anonymous read. Every
developer and every CI job therefore needs a credential for the feed.

Use a **classic** personal access token with the **`read:packages`** scope. GitHub's own
NuGet registry documentation calls for a classic token; fine-grained tokens are not reliably
supported here, so reach for classic first if a restore fails to authenticate. Create one at
[Settings → Developer settings → Personal access tokens → Tokens (classic)](https://github.com/settings/tokens).

`read:packages` grants nothing beyond reading packages, though it does cover every package the
account can already see — so scope it to a machine account if that matters to you.

### The recommended setup: a committed `nuget.config`

Put this next to your solution file. It adds the feed and keeps the token out of the file:

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" protocolVersion="3" />
    <add key="fortitude" value="https://nuget.pkg.github.com/Timmoth/index.json" />
  </packageSources>

  <packageSourceCredentials>
    <fortitude>
      <add key="Username" value="%GITHUB_USERNAME%" />
      <add key="ClearTextPassword" value="%GITHUB_TOKEN%" />
    </fortitude>
  </packageSourceCredentials>

  <!--
    Without this, every restore asks the GitHub feed about every package, which is slow and
    turns a bad token into a failure on packages that have nothing to do with Fortitude.
    With it, only Fortitude is looked for there.
  -->
  <packageSourceMapping>
    <packageSource key="nuget.org">
      <package pattern="*" />
    </packageSource>
    <packageSource key="fortitude">
      <package pattern="Fortitude" />
    </packageSource>
  </packageSourceMapping>
</configuration>
```

`%GITHUB_USERNAME%` and `%GITHUB_TOKEN%` are expanded from the environment, so the file is safe
to commit. Each developer sets them once in their shell profile:

```bash
export GITHUB_USERNAME=your-github-username
export GITHUB_TOKEN=ghp_your_classic_token_with_read_packages
```

On Windows (PowerShell):

```powershell
setx GITHUB_USERNAME "your-github-username"
setx GITHUB_TOKEN "ghp_your_classic_token_with_read_packages"
```

> If the variables are missing, NuGet passes the literal `%GITHUB_TOKEN%` as the password and
> the restore fails with a 401. See [Troubleshooting](#troubleshooting).

### Then install

```bash
dotnet add package Fortitude
```

---

## Per-tool setup

If you would rather not commit a `nuget.config`, each tool can hold the source itself. The
feed URL is the same everywhere:

```
https://nuget.pkg.github.com/Timmoth/index.json
```

and the credentials are always **your GitHub username** plus the **classic token** as the
password.

### .NET CLI

```bash
dotnet nuget add source https://nuget.pkg.github.com/Timmoth/index.json \
  --name fortitude \
  --username <your-github-username> \
  --password <classic-token-with-read:packages> \
  --store-password-in-clear-text
```

`--store-password-in-clear-text` is required on macOS and Linux, where NuGet has no encrypted
credential store. The value lands in your user-level `NuGet.Config`
(`~/.nuget/NuGet/NuGet.Config`), not in the repository.

Check it took:

```bash
dotnet nuget list source
```

### Visual Studio (Windows)

1. **Tools → NuGet Package Manager → Package Manager Settings**.
2. Select **Package Sources**, then **+**.
3. Name `fortitude`, Source `https://nuget.pkg.github.com/Timmoth/index.json`. **Update**, then **OK**.
4. The first restore shows a credentials prompt. Enter your GitHub username and paste the
   classic token as the password.

If the prompt never appears and the restore just fails, Windows has cached a bad credential:
open **Credential Manager → Windows Credentials** and delete any entry for
`nuget.pkg.github.com`, then restore again.

### Visual Studio Code

VS Code has no NuGet source UI. The C# Dev Kit and the `dotnet` CLI both read `nuget.config`,
so use either the committed file above or the CLI command, and VS Code picks it up.

If VS Code was already open, run **Developer: Reload Window** afterwards so the language
server re-reads the configuration.

### JetBrains Rider

1. **Settings → Build, Execution, Deployment → NuGet**.
2. On the **Sources** tab, **+** under *Feeds*.
3. Name `fortitude`, URL `https://nuget.pkg.github.com/Timmoth/index.json`.
4. Fill **User** and **Password** in the same dialog with your GitHub username and the classic
   token, then **Save**.
5. **Credentials** tab lets you correct a stored password later.

### CI

In GitHub Actions the repository's own token works and no secret is needed:

```yaml
- run: dotnet nuget add source https://nuget.pkg.github.com/Timmoth/index.json
    --name fortitude
    --username ${{ github.actor }}
    --password ${{ secrets.GITHUB_TOKEN }}
    --store-password-in-clear-text
```

Anywhere else — Azure DevOps, TeamCity, GitLab — store a classic `read:packages` token as a
secret and pass it the same way, or set `GITHUB_USERNAME`/`GITHUB_TOKEN` and rely on the
committed `nuget.config`.

---

## The server image

```bash
# Pull
docker pull ghcr.io/timmoth/fortitude:latest

# A single fixed port
docker run -e PORT=5005 -p 5005:5005 ghcr.io/timmoth/fortitude:latest

# A port range — each connecting client reserves one, which is what keeps
# parallel tests isolated from each other
docker run -e PORTS=5000-5005 -p 5000-5005:5000-5005 ghcr.io/timmoth/fortitude:latest

# With YAML handler definitions mounted in
docker run -e PORT=5005 -p 5005:5005 -v ./config:/app/config ghcr.io/timmoth/fortitude:latest
```

The image is public, so `docker pull` needs no login. Pulling a specific release is
`ghcr.io/timmoth/fortitude:v10.2.0`; `linux/amd64` and `linux/arm64` are both built.

Confirm it is up:

```bash
curl http://localhost:5005/fortitude/health   # -> server running
```

Then open `http://localhost:5005/fortitude` for the live request monitor.

Every `PORT`, `PORTS` and `Settings:*` value is listed in
[configuration](./configuration.md).

### Without Docker

```bash
git clone https://github.com/Timmoth/Fortitude.git
cd Fortitude
dotnet run --project ./Fortitude.Server
```

See [local-development](./local-development.md) for working on Fortitude itself, and
[aspire-integration-tests](./aspire-integration-tests.md) or the `Examples/` directory for
starting the server from a test run rather than by hand.

---

## Troubleshooting

**`401 (Unauthorized)` on restore.** The token is missing, expired, or fine-grained rather
than classic. Confirm the variables are actually set in the shell doing the restore
(`echo $GITHUB_TOKEN`) — an IDE launched from the desktop does not always inherit a shell
profile, which is the usual reason it works in the terminal but not in the IDE.

**`403 (Forbidden)`.** The token is valid but lacks `read:packages`.

**`Unable to load the service index for source`.** Usually a typo in the URL: the path segment
is the **owner** (`Timmoth`), not the repository.

**The restore asks the GitHub feed about unrelated packages.** Add the `packageSourceMapping`
block above; without it NuGet queries every source for every package.

**Windows keeps reusing a bad credential.** Delete the `nuget.pkg.github.com` entry from
Credential Manager → Windows Credentials.

**`docker pull` returns 401 or 404.** The image is public, so this should not happen. If a
future release creates a new package it starts private by default, and the owner has to change
the visibility once on its
[package settings page](https://github.com/Timmoth/Fortitude/pkgs/container/fortitude).
