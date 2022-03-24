using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;
using Mono.Unix;
using Mono.Unix.Native;
using System.Linq;

namespace HelloWorldService
{
    public class SignalTrapper : BackgroundService
    {
        public SignalTrapper(ILogger<SignalTrapper> logger, IHostApplicationLifetime appLifetime)
        {
            this._logger = logger;
            this._appLifetime = appLifetime;
        }

        private readonly ILogger<SignalTrapper> _logger;
        private readonly IHostApplicationLifetime _appLifetime;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (Environment.OSVersion.Platform == PlatformID.Unix)
            {
                // when UnixSignal is created, ApplicationLifetime won't response exit signal properly, we should always manually stop the application
                var allSignals = GetTrackingSignals();
                this._logger.LogInformation($"Tracking {allSignals.Length} signals.");

                while (!stoppingToken.IsCancellationRequested)
                {
                    var signal = await Task.Run(() =>
                    {
                        int index = UnixSignal.WaitAny(allSignals, TimeSpan.FromSeconds(30));
                        return index > -1 && index < allSignals.Length ? allSignals[index] : null;
                    });
                    
                    if (signal != null)
                    {
                        this._logger.LogInformation("Receive signal {0}({1})", signal.Signum, (int)signal.Signum);
                        signal.Reset();
                        this._appLifetime.StopApplication();
                        break;
                    }
                }
            }
            else
            {
                this._logger.LogInformation($"Platform {Environment.OSVersion.Platform} detected, skipping SignalTrapper.");
            }
        }

        private UnixSignal[] GetTrackingSignals()
        {
            return new[]
            {
                Signum.SIGINT,
                Signum.SIGTERM,
                Signum.SIGKILL
            }.Select(signum =>
            {
                try
                {
                    return new UnixSignal(signum);
                }
                catch
                {
                    return null;
                }
            }).Where(sig => sig != null)
            .ToArray();
        }
    }
}
