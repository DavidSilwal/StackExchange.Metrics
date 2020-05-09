using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

namespace StackExchange.Metrics.Infrastructure
{
    /// <summary>
    /// Collection of metrics that ensures uniqueness.
    /// </summary>
    internal class MetricCollection : IEnumerable<MetricBase>
    {
        private readonly string _prefix;
        private readonly IReadOnlyDictionary<string, string> _defaultTags;
        private readonly TagValueConverterDelegate _tagValueConverter;
        private readonly Func<string, string> _propertyToTagConverter;
        private readonly object _metricsLock = new object();
        private ImmutableList<MetricBase> _metrics = ImmutableList<MetricBase>.Empty;
        // this dictionary is to avoid duplicate metrics
        private readonly Dictionary<MetricKey, MetricBase> _rootNameAndTagsToMetric = new Dictionary<MetricKey, MetricBase>(MetricKeyComparer.Default);
        // All of the names which have been claimed, including the metrics which may have multiple suffixes, mapped to their root metric name.
        // This is to prevent suffix collisions with other metrics.
        private readonly Dictionary<string, string> _nameAndSuffixToRootName = new Dictionary<string, string>();

        public MetricCollection(
            string prefix,
            IReadOnlyDictionary<string, string> defaultTags,
            TagValueConverterDelegate tagValueConverter,
            Func<string, string> propertyToTagConverter
        )
        {
            _prefix = prefix;
            _defaultTags = defaultTags;
            _tagValueConverter = tagValueConverter;
            _propertyToTagConverter = propertyToTagConverter;
        }

        public int Count => _metrics.Count;

        public void Add(MetricBase metric)
        {
            var metricType = metric.GetType();
            var name = _prefix + metric.Name;
            var tags = MetricTag.Get(metric, _defaultTags, _tagValueConverter, _propertyToTagConverter);
            var key = new MetricKey(name, tags);

            lock (_metricsLock)
            {
                if (_nameAndSuffixToRootName.TryGetValue(name, out var rootName))
                {
                    throw new Exception(
                        $"Attempted to create metric name \"{name}\" with Type {metricType.FullName}. " +
                        $"This metric name is already in use as a suffix of Type {_rootNameAndTagsToMetric[key].GetType().FullName}.");
                }

                // claim all suffixes. Do this in two passes (check then add) so we don't end up in an inconsistent state.
                foreach (var s in metric.Suffixes)
                {
                    var nameWithSuffix = name + s;

                    // verify this is a valid metric name at all (it should be, since both parts are pre-validated, but just in case).
                    if (!MetricValidation.IsValidMetricName(nameWithSuffix))
                        throw new Exception($"\"{nameWithSuffix}\" is not a valid metric name");

                    if (_nameAndSuffixToRootName.TryGetValue(nameWithSuffix, out rootName) && !string.Equals(rootName, name, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new Exception(
                            $"Attempted to create metric name \"{nameWithSuffix}\" with Type {metricType.FullName}. " +
                            $"This metric name is already in use as a suffix of Type {_rootNameAndTagsToMetric[key].GetType().FullName}.");
                    }
                }

                foreach (var s in metric.Suffixes)
                {
                    _nameAndSuffixToRootName[metric.Name + s] = metric.Name;
                }

                if (_rootNameAndTagsToMetric.ContainsKey(key))
                {
                    throw new Exception($"Attempted to add duplicate metric with name \"{name}\".");
                }

                // metric doesn't exist yet.
                _rootNameAndTagsToMetric[key] = metric;
                _metrics = _metrics.Add(metric);
            }
        }

        public IEnumerator<MetricBase> GetEnumerator() => _metrics.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
