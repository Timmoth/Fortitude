# Security

## Reporting a vulnerability

Please do not open a public issue for a security problem. Report it privately through
[GitHub's security advisory form](https://github.com/Timmoth/Fortitude/security/advisories/new),
which lets us discuss and fix it before it becomes public.

Include what you can: affected version, how it is triggered, and what an attacker could do
with it. You should get an acknowledgement within a few days.

## What Fortitude is, and what it is not

Fortitude is a testing tool. It is built to run on a developer machine or inside a CI job,
against services you control. It is **not hardened for exposure to untrusted traffic**, and a
few parts are deliberately open because that is what makes it useful locally:

- **The live UI at `/fortitude` is unauthenticated** and can create, edit and delete handler
  definition files on the server's filesystem.
- **The admin API at `/fortitude/api` is unauthenticated** and can do the same over HTTP.
- **YAML handlers can execute JavaScript.** A `body.json` matcher is evaluated by the Jint
  engine. It runs sandboxed with a recursion limit and a 50ms timeout, but a handler
  definition is still code, so treat handler files like source.
- **Proxy mode forwards credentials.** `ProxiesTo` passes request headers, including
  `Authorization`, straight through to the upstream service.

### Running it somewhere less trusted

If the server has to be reachable beyond your own machine:

```
Settings:EnableAdminUi=false
Settings:EnableAdminApi=false
```

Both default to on. With them off, the server still mocks traffic and `/fortitude/health`
still answers, but nothing can reconfigure it over the network. Handler definitions then come
only from the mounted config directory.

Put it behind your own authentication or network policy as well; Fortitude provides none.

## Recordings may contain secrets

Proxy recordings capture real response bodies. Review generated fixtures before committing
them - a response from a real API can carry tokens, personal data, or anything else the
service returns.

## Dependencies

NuGet audit runs over direct and transitive packages on every restore, and high or critical
advisories fail the build. Dependabot opens update pull requests weekly.
