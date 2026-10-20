using System.Globalization;
using System.Net;
using McDiscovery.FakeServer;

// Usage: mcd-fakeserver [--bind 0.0.0.0] [--java-port 25565] [--bedrock-port 19132] [--offline]
// Runs fake Java (status, legacy, login, query) and Bedrock servers until Ctrl+C, for trying the stack locally.
var bind = IPAddress.Any;
var javaPort = 25565;
var bedrockPort = 19132;
var login = LoginBehavior.Encryption;
for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--bind":
            bind = IPAddress.Parse(args[++i]);
            break;
        case "--java-port":
            javaPort = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--bedrock-port":
            bedrockPort = int.Parse(args[++i], CultureInfo.InvariantCulture);
            break;
        case "--offline":
            login = LoginBehavior.Success;
            break;
        default:
            Console.Error.WriteLine($"Unknown argument {args[i]}");
            return 2;
    }
}

await using var java = new FakeJavaServer(bind, javaPort) { Login = login };
await using var query = new FakeQueryServer(bind, java.Port);
await using var bedrock = new FakeBedrockServer(bind, bedrockPort);
Console.WriteLine($"Fake Java server on {bind}:{java.Port} (tcp + query udp), Bedrock on {bind}:{bedrock.Port} (udp). Ctrl+C to stop.");

var done = new TaskCompletionSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    done.TrySetResult();
};
AppDomain.CurrentDomain.ProcessExit += (_, _) => done.TrySetResult();
await done.Task;
return 0;
