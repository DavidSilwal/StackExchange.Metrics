using StackExchange.Metrics.Metrics;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace StackExchange.Metrics.Infrastructure
{
    /// <summary>
    /// The base class for all metrics (time series). Custom metric types may inherit from this directly. However, most users will want to inherit from a child
    /// class, such as <see cref="Counter"/> or <see cref="AggregateGauge"/>.
    /// </summary>
    public abstract class MetricBase
    {
        struct MetricTypeInfo
        {
            public bool NeedsPreSerialize;
        }

        static readonly Dictionary<Type, MetricTypeInfo> s_typeInfoCache = new Dictionary<Type, MetricTypeInfo>();

        static readonly ImmutableArray<string> s_singleEmptyStringArray = ImmutableArray.Create(string.Empty);

        /// <summary>
        /// <see cref="MetricType" /> value indicating the type of metric.
        /// </summary>
        public abstract MetricType MetricType { get; }

        /// <summary>
        /// An enumeration of metric name suffixes. In most cases, this will be a single-element collection where the value of the element is an empty string.
        /// However, some metric types may actually serialize as multiple time series distinguished by metric names with different suffixes. The only built-in
        /// metric type which does this is <see cref="AggregateGauge"/> where the suffixes will be things like "_avg", "_min", "_95", etc.
        /// </summary>
        public virtual ImmutableArray<string> Suffixes { get; } = s_singleEmptyStringArray;

        /// <summary>
        /// The metric name, excluding any suffixes
        /// </summary>
        public string Name { get;  }

        /// <summary>
        /// Indicates whether the prefix should be prepended to <see cref="Name"/> whenever the metric is serialized.
        /// </summary>
        public bool IncludePrefix { get; }
        /// <summary>
        /// Description of this metric (time series) which will be sent to metric handlers as metadata.
        /// </summary>
        public string Description { get;  }
        /// <summary>
        /// The units for this metric (time series) which will be sent to metric handlers as metadata. (example: "milliseconds")
        /// </summary>
        public string Unit { get;  }

        /// <summary>
        /// Instantiates the base class.
        /// </summary>
        protected MetricBase(string name, string unit = null, string description = null, bool includePrefix = true)
        {
            if (name == null || !MetricValidation.IsValidMetricName(name))
            {
                throw new ArgumentException(nameof(name), name + " is not a valid metric name. Only characters in the regex class [a-zA-Z0-9\\-_./] are allowed.");
            }

            Name = name;
            Unit = unit;
            Description = description;
            IncludePrefix = includePrefix;
        }

        internal MetricKey GetMetricKey(IReadOnlyDictionary<string, string> tags)
        {
            return new MetricKey(Name, tags);
        }

        /// <summary>
        /// Called once per suffix in order to get a description.
        /// </summary>
        public virtual string GetDescription(int suffixIndex)
        {
            return Description;
        }

        /// <summary>
        /// Called once per suffix in order to get the units.
        /// </summary>
        public virtual string GetUnit(int suffixIndex)
        {
            return Unit;
        }

        /// <summary>
        /// Returns an enumerable of <see cref="MetaData"/> which describes this metric.
        /// </summary>
        public virtual IEnumerable<MetaData> GetMetaData(string prefix, IReadOnlyDictionary<string, string> tags)
        {
            for (var i = 0; i < Suffixes.Length; i++)
            {
                var fullName = GetFullName(Name, prefix, Suffixes[i]);
                string metricType;
                switch (MetricType)
                {
                    case MetricType.Counter:
                    case MetricType.CumulativeCounter:
                        metricType = "counter";
                        break;
                    case MetricType.Gauge:
                        metricType = "gauge";
                        break;
                    default:
                        metricType = MetricType.ToString().ToLower();
                        break;

                }

                yield return new MetaData(fullName, MetadataNames.Rate, null, metricType);

                var desc = GetDescription(i);
                if (!string.IsNullOrEmpty(desc))
                    yield return new MetaData(fullName, MetadataNames.Description, tags, desc);

                var unit = GetUnit(i);
                if (!string.IsNullOrEmpty(unit))
                    yield return new MetaData(fullName, MetadataNames.Unit, null, unit);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void SerializeInternal(IMetricBatch writer, DateTime timestamp, string prefix, IReadOnlyDictionary<string, string> tags)
        {
            Serialize(writer, timestamp, prefix, tags);
        }

        /// <summary>
        /// Called when metrics should be serialized to a payload. You must call <see cref="WriteValue"/> in order for anything to be serialized.
        ///
        /// This is called in serial with all other metrics, so DO NOT do anything computationally expensive in this method. If you need to do expensive
        /// computations (e.g. sorting a bunch of data), do it in <see cref="PreSerialize"/> which is called in parallel prior to this method.
        /// </summary>
        /// <param name="writer">
        /// A reference to an opaque object. Pass this as the first parameter to <see cref="WriteValue"/>. DO NOT retain a reference to this object or use it
        /// in an asynchronous manner. It is only guaranteed to be in a valid state for the duration of this method call.
        /// </param>
        /// <param name="timestamp">The timestamp when serialization of all metrics started.</param>
        /// <param name="prefix">
        /// Prefix prepended to the metric when serializing.
        /// </param>
        /// <param name="tags">
        /// Tags to use for the metric when serializing.
        /// </param>
        protected abstract void Serialize(IMetricBatch writer, DateTime timestamp, string prefix, IReadOnlyDictionary<string, string> tags);

        internal bool NeedsPreserialize()
        {
            return GetMetricTypeInfo().NeedsPreSerialize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void PreSerializeInternal() => PreSerialize();

        /// <summary>
        /// If this method is overriden, it will be called shortly before <see cref="Serialize"/>. Unlike Serialize, which is called on all metrics in serial,
        /// PreSerialize is called in parallel, which makes it better place to do computationally expensive operations.
        /// </summary>
        protected virtual void PreSerialize()
        {
        }

        /// <summary>
        /// This method serializes a time series record/value. This method must only be called from within <see cref="Serialize"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        protected void WriteValue(IMetricBatch writer, double value, DateTime timestamp, string prefix, string suffix, IReadOnlyDictionary<string, string> tags)
        {
            writer.SerializeMetric(
                new MetricReading(
                    name: GetFullName(Name, IncludePrefix ? prefix : string.Empty, suffix),
                    type: MetricType,
                    value: value,
                    tags: tags,
                    timestamp: timestamp
                )
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static string GetFullName(string name, string prefix, string suffix)
        {
            if (string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(suffix))
            {
                return name;
            }
            else if (prefix == null)
            {
                prefix = string.Empty;
            }
            else if (suffix == null)
            {
                suffix = string.Empty;
            }

#if NETCOREAPP
            return string.Concat(prefix.AsSpan(), name.AsSpan(), suffix.AsSpan());
#else
            return string.Concat(prefix, name, suffix);
#endif
        }

        MetricTypeInfo GetMetricTypeInfo()
        {
            var type = GetType();
            if (s_typeInfoCache.TryGetValue(type, out var info))
                return info;

            lock (s_typeInfoCache)
            {
                if (s_typeInfoCache.TryGetValue(type, out info))
                    return info;

                var needsPreSerialize = type.GetMethod(nameof(PreSerialize), BindingFlags.Instance | BindingFlags.NonPublic).DeclaringType != typeof(MetricBase);

                info = s_typeInfoCache[type] = new MetricTypeInfo
                {
                    NeedsPreSerialize = needsPreSerialize,
                };

                return info;
            }
        }
    }
}
