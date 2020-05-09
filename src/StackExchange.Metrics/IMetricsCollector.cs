using System;
using System.Collections.Generic;
using StackExchange.Metrics.Infrastructure;

namespace StackExchange.Metrics
{
    /// <summary>
    /// Exposes functionality to create new metrics and to collect metrics.
    /// </summary>
    public partial interface IMetricsCollector
    {
        /// <summary>
        /// An event called immediately before metrics are serialized. If you need to take a pre-serialization action on an individual metric, you should
        /// consider overriding <see cref="MetricBase.PreSerialize"/> instead, which is called in parallel for all metrics. This event occurs before
        /// PreSerialize is called.
        /// </summary>
        event Action BeforeSerialization;
        /// <summary>
        /// An event called immediately after metrics are serialized. It includes an argument with post-serialization information.
        /// </summary>
        event Action<AfterSerializationInfo> AfterSerialization;
        /// <summary>
        /// An event called immediately after metrics are posted to a metric handler. It includes an argument with information about the POST.
        /// </summary>
        event Action<AfterSendInfo> AfterSend;

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
    }
}
