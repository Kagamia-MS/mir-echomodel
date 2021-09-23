using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace HelloWorldService
{
    public class Startup
    {
        readonly ConcurrentDictionary<int, SemaphoreSlim> rwLock = new ConcurrentDictionary<int, SemaphoreSlim>();
        private DateTimeOffset startHealthyTime;

        // This method gets called by the runtime. Use this method to add services to the container.
        // For more information on how to configure your application, visit https://go.microsoft.com/fwlink/?LinkID=398940
        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<HostOptions>(
                opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(15));
        }

        // This method gets called by the runtime. Use this method to configure the HTTP request pipeline.
        public void Configure(IApplicationBuilder app, IWebHostEnvironment env, IConfiguration configuration, ILogger<Startup> logger, IHostApplicationLifetime lifetime)
        {
            bool isCrash = configuration.GetValue<bool>("crash", false);
            if (isCrash)
            {
                throw new Exception("Crash on starting.");
            }

            bool isOOM = configuration.GetValue<bool>("oom", false);
            if (isOOM)
            {
                logger.LogWarning("Alloc 1024GB memory!");
                Alloc(1024 * 1024, 1024 * 1024, lifetime.ApplicationStopping);
                return;
            }

            int startDelaySecods = configuration.GetValue<int>("StartDelay", 0);
            if (startDelaySecods > 0)
            {
                startHealthyTime = DateTimeOffset.UtcNow.AddSeconds(startDelaySecods);
                logger.LogInformation("Start healthy from {0}", startHealthyTime);
            }

            var cts = new CancellationTokenSource();
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                bool isSingleThread = configuration.GetValue<bool>("IsSingleThread", false);
                logger.LogInformation("IsSingleThread: {0}", isSingleThread);
                if (isSingleThread)
                {
                    endpoints.Map("/score", this.HandleScoreSingleThread);
                }
                else
                {
                    endpoints.Map("/score", this.HandleScore);
                }

                endpoints.Map("/kill", async _ =>
                {
                    cts.Cancel();
                    await Task.Delay(10000);
                    lifetime.StopApplication();
                });
                endpoints.Map("/trace", this.HandleTrace);
                endpoints.Map("/healthz", ctx => this.Healthz(ctx, cts.Token));
                endpoints.Map("/alloc", this.HandleAlloc);
            });

            logger.LogInformation(@"Configurations:  --crash=true --oom=true --IsSingleThread=true --StartDelay=5
Api:
  GET /score?time=50&size=1024&chunk=1&statusCode=200&abort=1&waitReq=0
  GET /kill?time=10000
  GET /trace
  GET /alloc?size=1024&count=1
  GET /healthz");
        }

        public async Task HandleScoreSingleThread(HttpContext context)
        {
            var rwLock = this.GetLock(context);
            await rwLock.WaitAsync(context.RequestAborted);
            try
            {
                await HandleScore(context);
            }
            finally
            {
                rwLock.Release();
            }
        }

        public async Task HandleScore(HttpContext context)
        {
            int.TryParse(context.Request.Query["time"].FirstOrDefault(), out int sleepMilliseconds);
            long.TryParse(context.Request.Query["size"].FirstOrDefault(), out long responseBodySize);
            bool isChunk = int.TryParse(context.Request.Query["chunk"].FirstOrDefault(), out int chunkVal) ? chunkVal != 0 : false;
            bool isAbort = int.TryParse(context.Request.Query["abort"].FirstOrDefault(), out int abortVal) ? abortVal != 0 : false;
            bool isWaitRequest = int.TryParse(context.Request.Query["waitReq"].FirstOrDefault(), out int waitReqVal) ? waitReqVal != 0 : true; // by default we always wait request body
            var returnStatusCode = Enum.TryParse(context.Request.Query["statusCode"], out HttpStatusCode statusCodeVal) ? statusCodeVal : HttpStatusCode.OK;

            var cancellationToken = context.RequestAborted;

            if (isWaitRequest && context.Request.Body != null)
            {
                byte[] buffer = new byte[8192];
                while (await context.Request.Body.ReadAsync(buffer, 0, buffer.Length, cancellationToken) > 0)
                {
                    // ignore request body
                }
            }

            context.Response.StatusCode = (int)returnStatusCode;

            if (sleepMilliseconds > 0)
            {
                await HighResolutionDelay(sleepMilliseconds, cancellationToken);
            }

            if (responseBodySize > 0)
            {
                if (!isChunk)
                {
                    context.Response.GetTypedHeaders().ContentLength = responseBodySize;
                }
                await context.Response.StartAsync(cancellationToken);
                string content = string.Join("", Enumerable.Range(0, 4096).Select(_ => "OK"));
                while (responseBodySize > 0)
                {
                    int bufferLen = (int)Math.Min(responseBodySize, content.Length);
                    if (bufferLen < content.Length)
                    {
                        await context.Response.WriteAsync(content.Substring(0, bufferLen), cancellationToken);
                    }
                    else
                    {
                        await context.Response.WriteAsync(content, cancellationToken);
                    }
                    responseBodySize -= bufferLen;
                }
            }
            else
            {
                await context.Response.WriteAsync("OK!", cancellationToken);
            }

            if (isAbort)
            {
                context.Abort();
            }
        }

        public async Task HandleAlloc(HttpContext context)
        {
            var cancellationToken = context.RequestAborted;
            int.TryParse(context.Request.Query["size"].FirstOrDefault(), out int size);
            if (!int.TryParse(context.Request.Query["count"].FirstOrDefault(), out int count))
            {
                count = 1;
            }

            Alloc(size, count, cancellationToken);
            await context.Response.WriteAsync("OK!", cancellationToken);
        }

        private static void Alloc(int size, int count, CancellationToken cancellationToken)
        {
            IntPtr[] buf = new IntPtr[count];
            try
            {
                for (int i = 0; i < count; i++)
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    buf[i] = Marshal.AllocHGlobal(size);
                }
            }
            finally
            {
                for (int i = 0; i < count; i++)
                {
                    Marshal.FreeHGlobal(buf[i]);
                }
            }
            buf = null;
        }

        private SemaphoreSlim GetLock(HttpContext context)
        {
            int port = context.Connection.LocalPort;
            return rwLock.GetOrAdd(port, _ => new SemaphoreSlim(1, 1));
        }

        private static async Task HighResolutionDelay(int milliseconds, CancellationToken cancellationToken = default)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var ticks = TimeSpan.FromMilliseconds(milliseconds).Ticks;
            var sleepPrecision = TimeSpan.FromMilliseconds(20).Ticks;
            while (ticks - sw.Elapsed.Ticks > sleepPrecision)
            {
                await Task.Delay(1, cancellationToken);
            }

            while (ticks > sw.Elapsed.Ticks)
            {
                cancellationToken.ThrowIfCancellationRequested();
                Thread.SpinWait(32);
            }
            sw.Stop();
        }

        private async Task Healthz(HttpContext ctx, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested || DateTimeOffset.UtcNow < startHealthyTime)
            {
                ctx.Response.StatusCode = 500;
                await ctx.Response.WriteAsync("Unhealthy");
            }
            else
            {
                ctx.Response.StatusCode = 200;
                await ctx.Response.WriteAsync("Healthy");
            }
        }

        private async Task HandleTrace(HttpContext ctx)
        {
            var req = ctx.Request;
            var conn = ctx.Connection;
            var cancellationToken = ctx.RequestAborted;
            await ctx.Response.WriteAsync($"{conn.RemoteIpAddress}:{conn.RemotePort} -> {conn.LocalIpAddress}:{conn.LocalPort}\n", cancellationToken);
            await ctx.Response.WriteAsync($"{req.Method} {req.Path}{req.QueryString} {req.Protocol}\n", cancellationToken);
            await ctx.Response.WriteAsync($"Headers:\n", cancellationToken);
            foreach (var kv in ctx.Request.Headers.OrderBy(kv => kv.Key))
            {
                await ctx.Response.WriteAsync($"  {kv.Key}: {kv.Value}\n", cancellationToken);
            }
        }
    }
}
