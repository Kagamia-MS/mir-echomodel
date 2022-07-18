using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace HelloWorldService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            try
            {
                PrintHelp();
                CreateHostBuilder(args).Build().Run();
            }
            catch(Exception ex)
            {
                WriteKubernetesErrorLog(ex);
                throw;
            }
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>()
                        .UseKestrel((ctx, options) =>
                        {
                            options.Limits.MaxRequestBodySize = null;
                            // https://github.com/dotnet/aspnetcore/pull/34013
                            options.AllowAlternateSchemes = true;
                            if (ctx.Configuration.GetValue<bool>("useHTTP2", false))
                            {
                                string http2Endpoint = ctx.Configuration.GetValue<string>("http2Endpoint");
                                options.ConfigureEndpointDefaults(listenOptions =>
                                {
                                    if (string.IsNullOrEmpty(http2Endpoint) || http2Endpoint.Contains(listenOptions.IPEndPoint.ToString()))
                                    {
                                        listenOptions.Protocols = HttpProtocols.Http2;
                                    }
                                });
                            }
                        });
                }).ConfigureServices(services =>
                {
                    services.AddHostedService<SignalTrapper>();
                });

        private static void PrintHelp()
        {
            var helpText = new[]
            {
                "version: v20220610-1",
                "Configurations:  --crash=true --oom=true --IsSingleThread=true --StartDelay=5 --useHTTP2=true --http2Endpoint=0.0.0.0:5000",
                "Api:",
                "  GET,POST /score?time=50&size=1024&chunk=1&statusCode=200&abort=1&waitReq=0&appendHeader=name:value",
                "  GET      /kill?time=10000",
                "  GET      /trace",
                "  GET      /alloc?size=1024&count=1&free=1",
                "  GET      /healthz"
            };

            foreach (var line in helpText)
            {
                Console.WriteLine(line);
            }
            Console.WriteLine();
        }

        private static void WriteKubernetesErrorLog(Exception ex)
        {
            const string terminationLog = "/dev/termination-log";
            try
            {
                if (File.Exists(terminationLog))
                {
                    File.AppendAllText(terminationLog, $"{ex.GetType().FullName}: {ex.Message}");
                }
            }
            catch
            {
                // ignore any IO error
            }
        }
    }
}
