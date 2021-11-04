using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace HelloWorldService
{
    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<Startup>()
                        .UseKestrel((ctx, options) =>
                        {
                            options.Limits.MaxRequestBodySize = null;
                            if (ctx.Configuration.GetValue<bool>("useHTTP2", true))
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
                });
    }
}
