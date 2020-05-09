using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using StackExchange.Metrics.Infrastructure;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StackExchange.Metrics
{
    /// <summary>
    /// The primary class for collecting metrics. Use this class to create metrics for reporting to various metric handlers.
    /// </summary>
    public partial class MetricsCollector : IMetricsCollector, IHostedService
    {
        private readonly ImmutableArray<MetricEndpoint> _endpoints;
        private readonly ImmutableArray<MetricSource> _sources;
        private readonly MetricCollection _metrics;
        private readonly List<MetricBase> _metricsNeedingPreSerialize = new List<MetricBase>();

        private bool _hasNewMetadata;
        private DateTime _lastMetadataFlushTime = DateTime.MinValue;
        private Task _flushTask;
        private Task _reportingTask;
        private CancellationTokenSource _shutdownTokenSource;

        /// <summary>
        /// If provided, all metric names will be prefixed with this value. This gives you the ability to keyspace your application. For example, you might
        /// want to use something like "app1.".
        /// </summary>
        public string MetricsNamePrefix { get; }
        /// <summary>
        /// If true, we will generate an exception every time posting to the a metrics endpoint fails with a server error (response code 5xx).
        /// </summary>
        public bool ThrowOnPostFail { get; set; }
        /// <summary>
        /// If true, we will generate an exception when the metric queue is full. This would most commonly be caused by an extended outage of the
        /// a metric handler. It is an indication that data is likely being lost.
        /// </summary>
        public bool ThrowOnQueueFull { get; set; }
        /// <summary>
        /// The length of time between metric reports (snapshots).
        /// </summary>
        public TimeSpan ReportingInterval { get; }
        /// <summary>
        /// The length of time between flush operations to an endpoint.
        /// </summary>
        public TimeSpan FlushInterval { get; }
        /// <summary>
        /// Number of times to retry a flush operation before giving up.
        /// </summary>
        public int RetryCount { get; }
        /// <summary>
        /// The length of time to wait before retrying a failed flush operation to an endpoint.
        /// </summary>
        public TimeSpan RetryInterval { get; }
        /// <summary>
        /// Allows you to specify a function which takes a tag name and value, and returns a possibly altered value. This could be used as a global sanitizer
        /// or normalizer. It is applied to all tag values, including default tags. If the return value is not a valid OpenTSDB tag, an exception will be
        /// thrown. Null values are possible for the tagValue argument, so be sure to handle nulls appropriately.
        /// </summary>
        public TagValueConverterDelegate TagValueConverter { get; }
        /// <summary>
        /// A list of tag names/values which will be automatically inculuded on every metric. The IgnoreDefaultTags attribute can be used on classes inheriting
        /// from MetricBase to exclude default tags. If an inherited class has a conflicting MetricTag field, it will override the default tag value. Default
        /// tags will generally not be included in metadata.
        /// </summary>
        public IReadOnlyDictionary<string, string> DefaultTags { get; }

        /// <summary>
        /// Exceptions which occur on a background thread within the collector will be passed to this delegate.
        /// </summary>
        public Action<Exception> ExceptionHandler { get; }

        /// <summary>
        /// An event called immediately before metrics are serialized. If you need to take a pre-serialization action on an individual metric, you should
        /// consider overriding <see cref="MetricBase.PreSerialize"/> instead, which is called in parallel for all metrics. This event occurs before
        /// PreSerialize is called.
        /// </summary>
        public event Action BeforeSerialization;
        /// <summary>
        /// An event called immediately after metrics are serialized. It includes an argument with post-serialization information.
        /// </summary>
        public event Action<AfterSerializationInfo> AfterSerialization;
        /// <summary>
        /// An event called immediately after metrics are posted to a metric handler. It includes an argument with information about the POST.
        /// </summary>
        public event Action<AfterSendInfo> AfterSend;

        /// <summary>
        /// True if <see cref="Stop"/> has been called on this collector.
        /// </summary>
        public bool ShutdownCalled => _shutdownTokenSource?.IsCancellationRequested ?? true;

        /// <summary>
        /// Enumerable of all metrics managed by this collector.
        /// </summary>
        public IEnumerable<MetricBase> Metrics => _metrics.AsEnumerable();

        /// <summary>
        /// Enumerable of all endpoints managed by this collector.
        /// </summary>
        public IEnumerable<MetricEndpoint> Endpoints => _endpoints.AsEnumerable();

        /// <summary>
        /// Enumerable of all sources consumed by this collector.
        /// </summary>
        public IEnumerable<MetricSource> Sources => _sources.AsEnumerable();

        /// <summary>
        /// Instantiates a new collector. You should typically only instantiate one collector for the lifetime of your
        /// application. It will manage the serialization of metrics and sending data to metric handlers.
        /// </summary>
        /// <param name="options">
        /// <see cref="MetricsCollectorOptions" /> representing the options to use for this collector.
        /// </param>
        [ActivatorUtilitiesConstructor]
        public MetricsCollector(IOptions<MetricsCollectorOptions> options) : this(options.Value)
        {
        }

        /// <summary>
        /// Instantiates a new collector. You should typically only instantiate one collector for the lifetime of your
        /// application. It will manage the serialization of metrics and sending data to metric handlers.
        /// </summary>
        /// <param name="options">
        /// <see cref="MetricsCollectorOptions" /> representing the options to use for this collector.
        /// </param>
        public MetricsCollector(MetricsCollectorOptions options)
        {
            ExceptionHandler = options.ExceptionHandler ?? (_ => { });
            MetricsNamePrefix = options.MetricsNamePrefix ?? "";
            if (MetricsNamePrefix != "" && !MetricValidation.IsValidMetricName(MetricsNamePrefix))
                throw new Exception("\"" + MetricsNamePrefix + "\" is not a valid metric name prefix.");

            ThrowOnPostFail = options.ThrowOnPostFail;
            ThrowOnQueueFull = options.ThrowOnQueueFull;
            ReportingInterval = options.SnapshotInterval;
            FlushInterval = options.FlushInterval;
            RetryInterval = options.RetryInterval;
            RetryCount = options.RetryCount;
            TagValueConverter = options.TagValueConverter;
            DefaultTags = ValidateDefaultTags(options.DefaultTags);

            _endpoints = options.Endpoints?.ToImmutableArray() ?? ImmutableArray<MetricEndpoint>.Empty;
            _sources = options.Sources?.ToImmutableArray() ?? ImmutableArray<MetricSource>.Empty;
            _metrics = new MetricCollection(MetricsNamePrefix, DefaultTags, options.TagValueConverter, options.PropertyToTagName);
            foreach (var metric in _sources.SelectMany(x => x.GetAll()))
            {
                _metrics.Add(metric);
                if (metric.NeedsPreserialize())
                {
                    _metricsNeedingPreSerialize.Add(metric);
                }
            }
        }

        /// <summary>
        /// Starts the collector.
        /// </summary>
        /// <remarks>
        /// This operation starts the snapshot and flush background tasks and attaches the collector
        /// to all of its metric sources.
        /// </remarks>
        public void Start()
        {
            // attach to all metric sources
            foreach (var source in _sources)
            {
                source.Attach(this);
                source.MetricAdded += OnMetricAdded;
            }

            _shutdownTokenSource = new CancellationTokenSource();

            // start background threads for flushing and snapshotting
            _flushTask = Task.Run(
                async () =>
                {
                    while (!_shutdownTokenSource.IsCancellationRequested)
                    {
                        await Task.Delay(FlushInterval);

                        try
                        {
                            await FlushAsync();
                        }
                        catch (Exception ex)
                        {
                            SendExceptionToHandler(ex);
                        }
                    }
                });

            _reportingTask = Task.Run(
                async () =>
                {
                    while (!_shutdownTokenSource.IsCancellationRequested)
                    {
                        await Task.Delay(ReportingInterval);

                        try
                        {
                            await SnapshotAsync();
                        }
                        catch (Exception ex)
                        {
                            SendExceptionToHandler(ex);
                        }
                    }
                });
        }

        /// <summary>
        /// Stops the collector.
        /// </summary>
        /// <remarks>
        /// This operation cancels the snapshot and flush background tasks, detaches the collector
        /// from all of its metric sources and waits for background tasks to complete.
        /// </remarks>
        public void Stop()
        {
            Debug.WriteLine("StackExchange.Metrics: Shutting down MetricsCollector.");

            // notify background tasks to stop
            _shutdownTokenSource.Cancel();

            // clean-up all metric endpoints
            foreach (var endpoint in _endpoints)
            {
                endpoint.Handler.Dispose();
            }

            // and detach from all sources
            foreach (var source in _sources)
            {
                source.MetricAdded -= OnMetricAdded;
                source.Detach(this);
            }
        }

        Task IHostedService.StartAsync(CancellationToken cancellationToken)
        {
            Start();
            return Task.CompletedTask;
        }

        Task IHostedService.StopAsync(CancellationToken cancellationToken)
        {
            Stop();
            return Task.CompletedTask;
        }

        private void OnMetricAdded(object sender, MetricBase metric)
        {
            _metrics.Add(metric);
            if (metric.NeedsPreserialize())
            {
                _metricsNeedingPreSerialize.Add(metric);
            }
        }

        private IReadOnlyDictionary<string, string> ValidateDefaultTags(IReadOnlyDictionary<string, string> tags)
        {
            var defaultTags = tags?.ToImmutableDictionary() ?? ImmutableDictionary<string, string>.Empty;
            var defaultTagBuilder = defaultTags.ToBuilder();
            foreach (var key in defaultTags.Keys.ToArray())
            {
                if (!MetricValidation.IsValidTagName(key))
                    throw new Exception($"\"{key}\" is not a valid tag name.");

                if (TagValueConverter != null)
                    defaultTagBuilder[key] = TagValueConverter(key, defaultTags[key]);

                if (!MetricValidation.IsValidTagValue(defaultTags[key]))
                    throw new Exception($"\"{defaultTags[key]}\" is not a valid tag value.");
            }

            return defaultTagBuilder.ToImmutable();
        }

        private Task SnapshotAsync()
        {
            try
            {
                var beforeSerialization = BeforeSerialization;
                if (beforeSerialization?.GetInvocationList().Length != 0)
                    beforeSerialization();

                IReadOnlyList<MetaData> metadata = Array.Empty<MetaData>();
                if (_hasNewMetadata || DateTime.UtcNow - _lastMetadataFlushTime >= TimeSpan.FromDays(1))
                {
                    metadata = GatherMetaData();
                }

                // prep all metrics for serialization
                var timestamp = DateTime.UtcNow;
                if (_metricsNeedingPreSerialize.Count > 0)
                {
                    Parallel.ForEach(_metricsNeedingPreSerialize, m => m.PreSerializeInternal());
                }

                var sw = new Stopwatch();
                foreach (var endpoint in _endpoints)
                {
                    sw.Restart();
                    SerializeMetrics(endpoint, timestamp, out var metricsCount, out var bytesWritten);
                    // We don't want to send metadata more frequently than the snapshot interval, so serialize it out if we need to
                    if (metadata.Count > 0)
                    {
                        SerializeMetadata(endpoint, metadata);
                    }
                    sw.Stop();

                    AfterSerialization?.Invoke(
                        new AfterSerializationInfo
                        {
                            Endpoint = endpoint.Name,
                            Count = metricsCount,
                            BytesWritten = bytesWritten,
                            Duration = sw.Elapsed,
                        });
                }
            }
            catch (Exception ex)
            {
                SendExceptionToHandler(ex);
            }

            return Task.CompletedTask;
        }

        private async Task FlushAsync()
        {
            if (_endpoints.Length == 0)
            {
                Debug.WriteLine("StackExchange.Metrics: No endpoints. Dropping data.");
                return;
            }

            foreach (var endpoint in _endpoints)
            {
                Debug.WriteLine($"StackExchange.Metrics: Flushing metrics for {endpoint.Name}");

                try
                {
                    await endpoint.Handler.FlushAsync(
                        RetryInterval,
                        RetryCount,
                        // Use Task.Run here to invoke the event listeners asynchronously.
                        // We're inside a lock, so calling the listeners synchronously would put us at risk of a deadlock.
                        info => Task.Run(
                            () =>
                            {
                                info.Endpoint = endpoint.Name;
                                try
                                {
                                    AfterSend?.Invoke(info);
                                }
                                catch (Exception ex)
                                {
                                    SendExceptionToHandler(ex);
                                }
                            }
                        ),
                        ex => SendExceptionToHandler(ex)
                    );
                }
                catch (Exception ex)
                {
                    // this will be hit if a sending operation repeatedly fails
                    SendExceptionToHandler(ex);
                }
            }
        }

        private void SerializeMetrics(MetricEndpoint endpoint, DateTime timestamp, out long metricsCount, out long bytesWritten)
        {
            metricsCount = 0;
            bytesWritten = 0;
            if (_metrics.Count == 0)
                return;

            using (var batch = endpoint.Handler.BeginBatch())
            {
                foreach (var m in _metrics)
                {
                    var metricType = m.GetType();
                    if (_)
                    try
                    {
                        m.SerializeInternal(batch, timestamp, MetricsNamePrefix, m.GetTags(Defauylt);
                    }
                    catch (Exception ex)
                    {
                        ex.Data["Endpoint.Name"] = endpoint.Name;
                        ex.Data["Endpoint.Type"] = endpoint.Handler.GetType();

                        SendExceptionToHandler(ex);
                    }
                }

                metricsCount += batch.MetricsWritten;
                bytesWritten += batch.BytesWritten;
            }
        }

        private void SerializeMetadata(MetricEndpoint endpoint, IEnumerable<MetaData> metadata)
        {
            Debug.WriteLine("StackExchange.Metrics: Serializing metadata.");
            endpoint.Handler.SerializeMetadata(metadata);
            _lastMetadataFlushTime = DateTime.UtcNow;
            Debug.WriteLine("StackExchange.Metrics: Serialized metadata.");
        }

        private IReadOnlyList<MetaData> GatherMetaData()
        {
            var allMetadata = new List<MetaData>();
            foreach (var metric in _metrics)
            {
                if (metric == null)
                    continue;

                allMetadata.AddRange(metric.GetMetaData(MetricsNamePrefix));
            }

            _hasNewMetadata = false;
            return allMetadata;
        }

        private void SendExceptionToHandler(Exception ex)
        {
            if (!ShouldSendException(ex))
                return;

            try
            {
                ExceptionHandler(ex);
            }
            catch (Exception) { } // there's nothing else we can do if the user-supplied exception handler itself throws an exception
        }

        private bool ShouldSendException(Exception ex)
        {
            if (ex is MetricPostException post)
            {
                if (post.SkipExceptionHandler)
                {
                    return false;
                }

                if (ThrowOnPostFail)
                    return true;

                return false;
            }

            if (ex is MetricQueueFullException)
                return ThrowOnQueueFull;

            return true;
        }
    }

    /// <summary>
    /// Information about a metrics serialization pass.
    /// </summary>
    public class AfterSerializationInfo
    {
        /// <summary>
        /// Endpoint that we wrote data to.
        /// </summary>
        public string Endpoint { get; internal set; }
        /// <summary>
        /// The number of data points serialized. The could be less than or greater than the number of metrics managed by the collector.
        /// </summary>
        public long Count { get; internal set; }
        /// <summary>
        /// The number of bytes written to payload(s).
        /// </summary>
        public long BytesWritten { get; internal set; }
        /// <summary>
        /// The duration of the serialization pass.
        /// </summary>
        public TimeSpan Duration { get; internal set; }
        /// <summary>
        /// The time serialization started.
        /// </summary>
        public DateTime StartTime { get; }

        internal AfterSerializationInfo()
        {
            StartTime = DateTime.UtcNow;
        }
    }

    /// <summary>
    /// Information about a send to a metrics endpoint.
    /// </summary>
    public class AfterSendInfo
    {
        /// <summary>
        /// Endpoint that we sent data to.
        /// </summary>
        public string Endpoint { get; internal set; }
        /// <summary>
        /// Gets a <see cref="PayloadType" /> indicating the type of payload that was flushed.
        /// </summary>
        public PayloadType PayloadType { get; internal set; }
        /// <summary>
        /// The number of bytes in the payload. This does not include HTTP header bytes.
        /// </summary>
        public long BytesWritten { get; internal set; }
        /// <summary>
        /// The duration of the POST.
        /// </summary>
        public TimeSpan Duration { get; internal set; }
        /// <summary>
        /// True if the POST was successful. If false, <see cref="Exception"/> will be non-null.
        /// </summary>
        public bool Successful => Exception == null;
        /// <summary>
        /// Information about a POST failure, if applicable. Otherwise, null.
        /// </summary>
        public Exception Exception { get; internal set; }
        /// <summary>
        /// The time the POST was initiated.
        /// </summary>
        public DateTime StartTime { get; }

        internal AfterSendInfo()
        {
            StartTime = DateTime.UtcNow;
        }
    }
}
