using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyMapper
{
    public class MappingConfiguration
    {
        // 源类型+目标类型 -> 具体映射规则
        private readonly Dictionary<(Type SourceType, Type DestType), TypeMappingConfig> _typeConfigs
            = new Dictionary<(Type, Type), TypeMappingConfig>();

        /// <summary>
        /// 配置源类型到目标类型的映射规则
        /// </summary>
        public TypeMappingConfig CreateMap<TSource, TDestination>()
        {
            var key = (typeof(TSource), typeof(TDestination));
            if (!_typeConfigs.TryGetValue(key, out var config))
            {
                config = new TypeMappingConfig( this,typeof(TSource), typeof(TDestination));
                _typeConfigs[key] = config;
            }
            return config;
        }
        /// <summary>
        /// 获取已配置的映射规则
        /// </summary>
        public bool TryGetMappingConfig(Type sourceType, Type destType, out TypeMappingConfig? config)
        {
            return _typeConfigs.TryGetValue((sourceType, destType), out config);
        }
        /// <summary>
        /// 注册反向映射规则
        /// </summary>
        internal void RegisterReverseMap(Type sourceType, Type destType, TypeMappingConfig reverseConfig)
        {
            _typeConfigs[(sourceType, destType)] = reverseConfig;
        }
    }
}
