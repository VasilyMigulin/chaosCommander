using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Hosting;

// Azure Functions isolated worker (.NET 8) entry point.
var host = new HostBuilder()
    .ConfigureFunctionsWorkerDefaults()
    .Build();

host.Run();
