#if NETCOREAPP
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.Tracing;
using Microsoft.Diagnostics.NETCore.Client;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics.Metrics
{
    /// <summary>
    /// Implements <see cref="MetricSource" /> to provide information for
    /// ASP.NET Core applications:
    ///  - Requests per second
    ///  - Total requests
    ///  - Current requests
    ///  - Failed requests
    /// </summary>
    public sealed class AspNetMetricSet : MetricSource
    {
        private readonly ImmutableArray<MetricBase> _metrics;

        /// <summary>
        /// Constructs a new instance of <see cref="AspNetMetricSet" />.
        /// </summary>
        public AspNetMetricSet(IDiagnosticsCollector diagnosticsCollector)
        {
            const string MicrosoftAspNetCoreHostingEventSourceName = "Microsoft.AspNetCore.Hosting";

            diagnosticsCollector.AddSource(
                new EventPipeProvider(
                    MicrosoftAspNetCoreHostingEventSourceName,
                    EventLevel.Informational,
                    arguments: new Dictionary<string, string>()
                    {
                        { "EventCounterIntervalSec", "5" }
                    }
                )
            );

            var metricBuilder = ImmutableArray.CreateBuilder<MetricBase>();

            void AddCounterCallback(string name, Counter counter)
            {
                diagnosticsCollector.AddCounterCallback(MicrosoftAspNetCoreHostingEventSourceName, name, counter.Increment);
                metricBuilder.Add(counter);
            }

            void AddGaugeCallback(string name, SamplingGauge gauge)
            {
                diagnosticsCollector.AddGaugeCallback(MicrosoftAspNetCoreHostingEventSourceName, name, gauge.Record);
                metricBuilder.Add(gauge);
            }

            var requestsPerSec = Metric.Initialize<Counter>("dotnet.kestrel.requests.per_sec", "requests/sec", "Requests per second", includePrefix: false);
            var totalRequests = Metric.Initialize<SamplingGauge>("dotnet.kestrel.requests.total", "requests", "Total requests", includePrefix: false);
            var currentRequests = Metric.Initialize<SamplingGauge>("dotnet.kestrel.requests.current", "requests", "Currently executing requests", includePrefix: false);
            var failedRequests = Metric.Initialize<SamplingGauge>("dotnet.kestrel.requests.failed", "requests", "Failed requests", includePrefix: false);

            AddCounterCallback("requests-per-sec", requestsPerSec);
            AddGaugeCallback("total-requests", totalRequests);
            AddGaugeCallback("current-requests", currentRequests);
            AddGaugeCallback("failed-requests", failedRequests);

            _metrics = metricBuilder.ToImmutable();
        }

        /// <inheritdoc/>
        public override void Attach(IMetricsCollector collector)
        {
        }

        /// <inheritdoc/>
        public override void Detach(IMetricsCollector collector)
        {
        }

        /// <inheritdoc/>
        public override IEnumerable<MetricBase> GetAll() => _metrics;
    }
}
#endif
