using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MyMapper
{
    public class TypeMappingConfig
    {
        private readonly MappingConfiguration _rootConfig;
        public Type SourceType { get; }
        public Type DestType { get; }

        // 自定义规则存储
        private readonly Dictionary<string, Func<object, object?>> _ctorParamMappings = new();
        private readonly Dictionary<string, string> _propertyMappings = new();
        private readonly HashSet<string> _ignoredProperties = new();

        public TypeMappingConfig(MappingConfiguration rootConfig, Type sourceType, Type destType)
        {
            _rootConfig = rootConfig;
            SourceType = sourceType;
            DestType = destType;
        }

        #region 自定义规则配置
        public TypeMappingConfig ForCtorParam(string paramName, string sourcePropertyName)
        {
            _ctorParamMappings[paramName] = source =>
            {
                var prop = SourceType.GetProperty(sourcePropertyName,
                    BindingFlags.Public | BindingFlags.Instance);
                return prop?.GetValue(source);
            };
            return this;
        }

        public TypeMappingConfig ForCtorParam(string paramName, Func<object, object?> valueProvider)
        {
            _ctorParamMappings[paramName] = valueProvider;
            return this;
        }

        public TypeMappingConfig ForMember(string destPropertyName, string sourcePropertyName)
        {
            _propertyMappings[destPropertyName] = sourcePropertyName;
            return this;
        }

        public TypeMappingConfig IgnoreMember(string destPropertyName)
        {
            _ignoredProperties.Add(destPropertyName);
            return this;
        }
        #endregion

        #region 反向映射
        public TypeMappingConfig ReverseMap()
        {
            var reverseConfig = new TypeMappingConfig(_rootConfig, DestType, SourceType);

            // 反向复用自定义规则
            foreach (var (destProp, sourceProp) in _propertyMappings)
            {
                reverseConfig.ForMember(sourceProp, destProp);
            }
            foreach (var ignoredProp in _ignoredProperties)
            {
                var reverseProp = _propertyMappings.TryGetValue(ignoredProp, out var sourceProp)
                    ? sourceProp
                    : ignoredProp;
                reverseConfig.IgnoreMember(reverseProp);
            }

            _rootConfig.RegisterReverseMap(DestType, SourceType, reverseConfig);
            return reverseConfig;
        }
        #endregion

        #region 规则获取（无自定义规则时返回默认值）
        /// <summary>
        /// 获取构造参数值（有自定义规则用规则，无则默认匹配参数名=属性名）
        /// </summary>
        public object? GetCtorParamValue(string paramName, object source)
        {
            // 优先使用自定义规则
            if (_ctorParamMappings.TryGetValue(paramName, out var provider))
            {
                return provider(source);
            }

            // 无自定义规则：默认匹配参数名=源属性名
            var sourceProp = SourceType.GetProperty(paramName,
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase); // 忽略大小写
            return sourceProp?.GetValue(source);
        }

        /// <summary>
        /// 获取目标属性对应的源属性名（有自定义规则用规则，无则默认同名）
        /// </summary>
        public string GetSourcePropertyName(string destPropertyName)
        {
            // 优先使用自定义规则
            if (_propertyMappings.TryGetValue(destPropertyName, out var sourceName))
            {
                return sourceName;
            }

            // 无自定义规则：默认返回同名
            return destPropertyName;
        }

        /// <summary>
        /// 判断是否忽略属性（仅自定义规则生效）
        /// </summary>
        public bool IsIgnored(string destPropertyName)
        {
            return _ignoredProperties.Contains(destPropertyName);
        }
        #endregion
    }
}
