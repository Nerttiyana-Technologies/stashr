using Microsoft.Extensions.Configuration;
using Stashr.Client;
using Stashr.Configuration;

// Demonstrates consuming stashr from a .NET app via the SDK.
// Usage:  STASHR_TOKEN=<root token>  dotnet run --project samples/Stashr.Sample
//   (start the server first: dotnet run --project src/Stashr.Server)

var token = Environment.GetEnvironmentVariable("STASHR_TOKEN") ?? (args.Length > 0 ? args[0] : null);
if (string.IsNullOrWhiteSpace(token))
{
    Console.Error.WriteLine("Set STASHR_TOKEN (or pass the token as the first argument).");
    return 1;
}

var address = Environment.GetEnvironmentVariable("STASHR_ADDR") ?? "http://localhost:5000";
using var client = new StashrClient(new StashrClientOptions { Address = address, Token = token });

if (await client.IsSealedAsync())
{
    Console.Error.WriteLine("stashr is sealed — unseal it first.");
    return 1;
}

const string path = "sample/demo";
var written = await client.WriteKvAsync(path, new Dictionary<string, string>
{
    ["hello"] = "world",
    ["env"] = "dev",
});
Console.WriteLine($"wrote {path} (version {written})");

var read = await client.ReadKvAsync(path);
Console.WriteLine($"read {path} back from stashr:");
foreach (var kv in read!)
    Console.WriteLine($"  {kv.Key} = {kv.Value}");

// The headline: existing IConfiguration access, resolved from stashr with no code rewrite.
Console.WriteLine();
Console.WriteLine("via AddStashr() IConfiguration provider:");
var config = new ConfigurationBuilder()
    .AddStashr(o =>
    {
        o.Address = address;
        o.Token = token;
        o.AddPath(path);
    })
    .Build();
Console.WriteLine($"  config[\"hello\"] = {config["hello"]}");
Console.WriteLine($"  config[\"env\"]   = {config["env"]}");

return 0;
