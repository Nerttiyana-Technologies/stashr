![stashr](https://raw.githubusercontent.com/Nerttiyana-Technologies/stashr/main/assets/logo/stashr-banner.png)

# stashr

A fully .NET, open-source secrets engine — a Vault-compatible API with native .NET integration
and a built-in web UI.

> **Pre-audit release (0.9.0).** Not yet independently security-audited. Suitable for evaluation,
> internal tooling, and non-critical workloads. See the repository for the full security posture.

## Packages

- **Stashr.Client** — .NET SDK over the HTTP API.
- **Stashr.Configuration** — `AddStashr()` IConfiguration provider.
- **Stashr.Configuration.Legacy** — .NET Framework 4.8 `ConfigurationBuilder`.
- **Stashr.Cli** — the `stashr` operator CLI (dotnet tool).

## Install

```
dotnet add package Stashr.Client
dotnet add package Stashr.Configuration
dotnet tool install --global Stashr.Cli
```

## Use it

```csharp
// SDK
var client = new StashrClient(new Uri("https://stashr.internal:8200"), token);
var secret = await client.GetSecretAsync("app/db");
```

```csharp
// Bind secrets straight into IConfiguration — no plaintext in appsettings.json
builder.Configuration.AddStashr(o =>
{
    o.Address = new Uri("https://stashr.internal:8200");
    o.Token   = Environment.GetEnvironmentVariable("STASHR_TOKEN");
    o.Path    = "app/db";
});
```

For .NET Framework apps, register `Stashr.Configuration.Legacy.StashrConfigBuilder` in
`web.config` to resolve `appSettings` / `connectionStrings` from stashr.

## Links

- Source, docs, and the server (Docker image): https://github.com/Nerttiyana-Technologies/stashr
- License: Apache-2.0
