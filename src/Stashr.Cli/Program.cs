using Stashr.Client;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var addr = Environment.GetEnvironmentVariable("STASHR_ADDR") ?? "http://localhost:5000";
var token = Environment.GetEnvironmentVariable("STASHR_TOKEN");
using var client = new StashrClient(new StashrClientOptions { Address = addr, Token = token });

try
{
    return await Run(client, args);
}
catch (StashrException ex)
{
    Console.Error.WriteLine($"error: {ex.Message}");
    return 1;
}

static async Task<int> Run(StashrClient client, string[] args)
{
    switch (args[0])
    {
        case "status":
        {
            var s = await client.GetStatusAsync();
            Console.WriteLine($"initialized : {s.Initialized}");
            Console.WriteLine($"sealed      : {s.Sealed}");
            Console.WriteLine($"threshold   : {s.Threshold} of {s.TotalShares}");
            Console.WriteLine($"progress    : {s.Progress}");
            return 0;
        }
        case "operator": return await OperatorCmd(client, args);
        case "kv": return await KvCmd(client, args);
        case "audit": return await AuditCmd(client, args);
        case "policy": return await PolicyCmd(client, args);
        case "help" or "-h" or "--help": PrintUsage(); return 0;
        default:
            Console.Error.WriteLine($"unknown command: {args[0]}");
            PrintUsage();
            return 1;
    }
}

static async Task<int> OperatorCmd(StashrClient client, string[] args)
{
    if (args.Length < 2)
    {
        Console.Error.WriteLine("usage: stashr operator <init|unseal|seal>");
        return 1;
    }

    switch (args[1])
    {
        case "init":
        {
            var init = await client.InitAsync(GetIntOpt(args, "--shares", 5), GetIntOpt(args, "--threshold", 3));
            Console.WriteLine("Unseal keys (store securely — shown once):");
            for (var i = 0; i < init.Keys.Count; i++)
                Console.WriteLine($"  {i + 1}: {init.Keys[i]}");
            Console.WriteLine();
            Console.WriteLine($"Root token: {init.RootToken}");
            return 0;
        }
        case "unseal":
        {
            if (args.Length < 3)
            {
                Console.Error.WriteLine("usage: stashr operator unseal <key>");
                return 1;
            }
            var s = await client.UnsealAsync(args[2]);
            Console.WriteLine(s.Sealed ? $"still sealed — progress {s.Progress}/{s.Threshold}" : "unsealed");
            return 0;
        }
        case "seal":
            await client.SealAsync();
            Console.WriteLine("sealed");
            return 0;
        default:
            Console.Error.WriteLine($"unknown operator subcommand: {args[1]}");
            return 1;
    }
}

static async Task<int> KvCmd(StashrClient client, string[] args)
{
    if (args.Length < 3)
    {
        Console.Error.WriteLine("usage: stashr kv <get|put|list|delete> <path> [key=value ...]");
        return 1;
    }

    var path = args[2];
    switch (args[1])
    {
        case "get":
        {
            var data = await client.ReadKvAsync(path);
            if (data is null)
            {
                Console.Error.WriteLine("not found");
                return 1;
            }
            foreach (var kv in data) Console.WriteLine($"{kv.Key} = {kv.Value}");
            return 0;
        }
        case "put":
        {
            var data = new Dictionary<string, string>();
            for (var i = 3; i < args.Length; i++)
            {
                var eq = args[i].IndexOf('=');
                if (eq > 0) data[args[i].Substring(0, eq)] = args[i].Substring(eq + 1);
            }
            if (data.Count == 0)
            {
                Console.Error.WriteLine("provide at least one key=value");
                return 1;
            }
            var version = await client.WriteKvAsync(path, data);
            Console.WriteLine($"wrote {path} (version {version})");
            return 0;
        }
        case "list":
            foreach (var k in await client.ListKvAsync(path)) Console.WriteLine(k);
            return 0;
        case "delete":
            await client.DeleteKvAsync(path);
            Console.WriteLine($"deleted {path}");
            return 0;
        default:
            Console.Error.WriteLine($"unknown kv subcommand: {args[1]}");
            return 1;
    }
}

static async Task<int> AuditCmd(StashrClient client, string[] args)
{
    if (args.Length >= 2 && args[1] == "verify")
    {
        var r = await client.VerifyAuditAsync();
        if (r.Valid)
        {
            Console.WriteLine($"audit chain valid ({r.Checked} entries)");
            return 0;
        }
        Console.Error.WriteLine($"TAMPER DETECTED at seq {r.FirstBrokenSeq}");
        return 1;
    }
    Console.Error.WriteLine("usage: stashr audit verify");
    return 1;
}

static async Task<int> PolicyCmd(StashrClient client, string[] args)
{
    if (args.Length >= 2 && args[1] == "explain")
    {
        var path = GetStrOpt(args, "--path");
        var cap = GetStrOpt(args, "--capability");
        var pol = GetStrOpt(args, "--policies") ?? string.Empty;
        if (path is null || cap is null)
        {
            Console.Error.WriteLine("usage: stashr policy explain --path <p> --capability <c> [--policies a,b]");
            return 1;
        }
        var r = await client.ExplainAsync(path, cap, pol.Split(',', StringSplitOptions.RemoveEmptyEntries));
        Console.WriteLine($"{(r.Allowed ? "ALLOWED" : "DENIED")}: {r.Explanation}");
        return 0;
    }
    Console.Error.WriteLine("usage: stashr policy explain ...");
    return 1;
}

static int GetIntOpt(string[] args, string name, int fallback)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i] == name && int.TryParse(args[i + 1], out var v)) return v;
    return fallback;
}

static string? GetStrOpt(string[] args, string name)
{
    for (var i = 0; i < args.Length - 1; i++)
        if (args[i] == name) return args[i + 1];
    return null;
}

static void PrintUsage()
{
    Console.WriteLine(
        """
        stashr — command-line client for the stashr secrets engine

        Environment:
          STASHR_ADDR    server address (default http://localhost:5000)
          STASHR_TOKEN   auth token

        Commands:
          status                                      show seal status
          operator init [--shares N --threshold M]    initialize; prints unseal keys + root token
          operator unseal <key>                       submit an unseal key share
          operator seal                               seal the engine
          kv get <path>                               read a secret
          kv put <path> key=value [key=value ...]     write a secret
          kv list <path>                              list keys under a path
          kv delete <path>                            soft-delete the latest version
          audit verify                                verify the audit hash-chain
          policy explain --path <p> --capability <c> [--policies a,b]
        """);
}
