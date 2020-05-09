using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;

namespace StackExchange.Metrics.Infrastructure
{
    class MetricTag
    {
        private static ImmutableDictionary<Type, ImmutableArray<MetricTag>> _tagCache = ImmutableDictionary<Type, ImmutableArray<MetricTag>>.Empty;

        public string Name { get; }
        public bool IsFromDefault { get; }
        public bool IsOptional { get; }
        public MemberInfo MemberInfo { get; }
        public MetricTagAttribute Attribute { get; }

        /// <summary>
        /// Only use this constructor when creating a default tag.
        /// </summary>
        public MetricTag(string name)
        {
            Name = name;
            IsFromDefault = true;
            IsOptional = false;
        }

        /// <summary>
        /// Use this constructor when instantiating from a field or property.
        /// </summary>
        public MetricTag(MemberInfo memberInfo, MetricTagAttribute attribute, Func<string, string> nameReplacer)
        {
            switch (memberInfo)
            {
                case FieldInfo fieldInfo:
                    if (!fieldInfo.IsInitOnly || (fieldInfo.FieldType != typeof(string) && !fieldInfo.FieldType.IsEnum))
                    {
                        throw new InvalidOperationException(
                            $"The MetricTag attribute can only be applied to readonly string or enum fields. {memberInfo.DeclaringType.FullName}.{memberInfo.Name} is invalid."
                        );
                    }

                    break;
                case PropertyInfo propertyInfo:
                    if (propertyInfo.SetMethod != null ||
                        (propertyInfo.PropertyType != typeof(string) && !propertyInfo.PropertyType.IsEnum))
                    {
                        throw new InvalidOperationException(
                            $"The MetricTag attribute can only be applied to readonly string or enum properties. {memberInfo.DeclaringType.TFullName}.{memberInfo.Name} is invalid."
                        );
                    }

                    break;
                default:
                    throw new InvalidOperationException(
                        $"The MetricTag attribute can only be applied to properties or fields. {memberInfo.DeclaringType.FullName}.{memberInfo.Name} is invalid."
                    );
            }

            IsFromDefault = false;
            IsOptional = attribute.IsOptional;
            MemberInfo = memberInfo;
            Attribute = attribute;

            if (attribute.Name != null)
                Name = attribute.Name;
            else if (nameReplacer != null)
                Name = nameReplacer(memberInfo.Name);
            else
                Name = memberInfo.Name;

            if (!MetricValidation.IsValidTagName(Name))
            {
                throw new InvalidOperationException($"\"{Name}\" is not a valid tag name. Field: {memberInfo.DeclaringType.FullName}.{memberInfo.Name}.");
            }
        }

        public string GetValue(MetricBase metric)
        {
            switch (MemberInfo)
            {
                case PropertyInfo propertyInfo:
                    return propertyInfo.GetValue(metric)?.ToString();
                case FieldInfo fieldInfo:
                    return fieldInfo.GetValue(metric)?.ToString();
            }

            return null;
        }

        public static IReadOnlyDictionary<string, string> Get(
            MetricBase metric,
            IReadOnlyDictionary<string, string> defaultTags,
            TagValueConverterDelegate tagValueConverter,
            Func<string, string> propertyToTagConverter
        )
        {
            var tags = ImmutableDictionary.CreateBuilder<string, string>();
            foreach (var tag in GetTagsList(metric, defaultTags, propertyToTagConverter))
            {
                var value = tag.IsFromDefault ? defaultTags[tag.Name] : tag.GetValue(metric);
                if (tagValueConverter != null)
                    value = tagValueConverter(tag.Name, value);

                if (value == null)
                {
                    if (tag.IsOptional)
                        continue;

                    throw new InvalidOperationException(
                        $"null is not a valid tag value for {metric.GetType().FullName}.{tag.MemberInfo.Name}. This tag was declared as non-optional.");
                }
                if (!MetricValidation.IsValidTagValue(value))
                {
                    throw new InvalidOperationException(
                        $"Invalid value for tag {metric.GetType().FullName}.{tag.MemberInfo.Name}. \"{value}\" is not a valid tag value. " +
                        $"Only characters in the regex class [a-zA-Z0-9\\-_./] are allowed.");
                }

                tags.Add(tag.Name, value);
            }

            if (tags.Count == 0)
            {
                throw new InvalidOperationException(
                    $"At least one tag value must be specified for every metric. {metric.GetType().FullName} was instantiated without any tag values."
                );
            }

            return tags;
        }

        private static ImmutableArray<MetricTag> GetTagsList(
            MetricBase metric,
            IReadOnlyDictionary<string, string> defaultTags,
            Func<string, string> propertyToTagConverter
        )
        {
            var type = metric.GetType();
            if (_tagCache.TryGetValue(type, out var tags))
            {
                return tags;
            }

            // build list of tag members of the current type
            var members = type.GetMembers();
            var tagBuilder = ImmutableArray.CreateBuilder<MetricTag>();
            foreach (var member in members)
            {
                var metricTag = member.GetCustomAttribute<MetricTagAttribute>();
                if (metricTag != null)
                    tagBuilder.Add(new MetricTag(member, metricTag, propertyToTagConverter));
            }

            // get default tags
            var tagAttributes = GetTagAttributesData(type);
            if (tagAttributes.IncludeByDefault || tagAttributes.IncludeByTag?.Count > 0)
            {
                foreach (var name in defaultTags.Keys)
                {
                    var explicitInclude = false; // assignment isn't actually used, but the compiler complains without it
                    if (tagAttributes.IncludeByTag?.TryGetValue(name, out explicitInclude) == true)
                    {
                        if (!explicitInclude)
                            continue;
                    }
                    else
                    {
                        if (!tagAttributes.IncludeByDefault)
                            continue;
                    }

                    if (tagBuilder.Any(t => t.Name == name))
                        continue;

                    tagBuilder.Add(new MetricTag(name));
                }
            }

            if (tagBuilder.Count == 0)
                throw new TypeInitializationException(type.FullName, new Exception("Type does not contain any tags. Metrics must have at least one tag to be serializable."));

            tagBuilder.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            tags = tagBuilder.ToImmutable();
            _tagCache = _tagCache.SetItem(type, tags);
            return tags;
        }

        private readonly struct TagAttributesData
        {
            public TagAttributesData(bool includeByDefault, ImmutableDictionary<string, bool> includeByTag)
            {
                IncludeByDefault = includeByDefault;
                IncludeByTag = includeByTag;
            }

            public bool IncludeByDefault { get; }
            public ImmutableDictionary<string, bool> IncludeByTag { get; }
        }

        private static TagAttributesData GetTagAttributesData(Type type)
        {
            var foundDefault = false;
            var includeByDefault = true;
            var includeByTag = ImmutableDictionary<string, bool>.Empty;

            var objType = typeof(object);

            while (true)
            {
                var exclude = type.GetCustomAttribute<ExcludeDefaultTagsAttribute>(false);
                var restore = type.GetCustomAttribute<RestoreDefaultTagsAttribute>(false);

                if (restore?.Tags.Length == 0)
                {
                    foundDefault = true;
                    includeByDefault = true;
                }
                else if (exclude?.Tags.Length == 0)
                {
                    foundDefault = true;
                    includeByDefault = false;
                }

                if (restore?.Tags.Length > 0)
                {
                    foreach (var tag in restore.Tags)
                    {
                        includeByTag = includeByTag.SetItem(tag, true);
                    }
                }

                if (exclude?.Tags.Length > 0)
                {
                    foreach (var tag in exclude.Tags)
                    {
                        includeByTag = includeByTag.SetItem(tag, true);
                    }
                }

                if (foundDefault)
                    break;

                type = type.BaseType;
                if (type == objType || type == null)
                    break;
            }

            return new TagAttributesData(includeByDefault, includeByTag);
        }
    }
}
