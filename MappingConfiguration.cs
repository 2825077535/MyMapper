using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
            if (_typeConfigs.TryGetValue(key, out var config))
            {
                throw new MappingConfigException($"已存在 {typeof(TSource).Name}→{typeof(TDestination).Name} 的映射配置", typeof(TSource), typeof(TDestination));
            }
            config = new TypeMappingConfig(this, typeof(TSource), typeof(TDestination));
            _typeConfigs[key] = config;
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
        /// <summary>
        /// 校验所有映射规则的合法性（启动时执行）
        /// </summary>
        public void Validate()
        {
            var errors = new List<string>();
            var constructorSelector = new DefaultConstructorSelector();

            foreach (var (key, config) in _typeConfigs)
            {
                var sourceType = key.SourceType;
                var destType = key.DestType;

                // 目标类型必须有公共构造函数
                try
                {
                    constructorSelector.SelectBestConstructor(destType);
                }
                catch (Exception ex)
                {
                    errors.Add($"映射 {sourceType.Name}→{destType.Name} 失败：{ex.Message}");
                }

                //自定义属性映射的源/目标属性必须存在
                foreach (var (destProp, sourceProp) in config._propertyMappings)
                {
                    if (destType.GetProperty(destProp, BindingFlags.Public | BindingFlags.Instance) == null)
                    {
                        errors.Add($"映射 {sourceType.Name}→{destType.Name} 失败：目标属性 {destProp} 不存在");
                    }
                    if (sourceType.GetProperty(sourceProp, BindingFlags.Public | BindingFlags.Instance) == null)
                    {
                        errors.Add($"映射 {sourceType.Name}→{destType.Name} 失败：源属性 {sourceProp} 不存在");
                    }
                }

                //忽略的属性必须存在
                foreach (var ignoredProp in config._ignoredProperties)
                {
                    if (destType.GetProperty(ignoredProp, BindingFlags.Public | BindingFlags.Instance) == null)
                    {
                        errors.Add($"映射 {sourceType.Name}→{destType.Name} 失败：忽略的属性 {ignoredProp} 不存在");
                    }
                }
            }

            if (errors.Any())
            {
                throw new MappingValidationException("映射规则校验失败，共发现 " + errors.Count + " 个错误", errors);
            }
        }

        /// <summary>
        /// 获取映射规则诊断信息（调试用）
        /// </summary>
        public string GetDiagnostics()
        {
            var sb = new StringBuilder();
            sb.AppendLine("===== MiniMapper 映射规则诊断报告 =====");
            sb.AppendLine($"总映射规则数：{_typeConfigs.Count}");
            sb.AppendLine();

            foreach (var (key, config) in _typeConfigs)
            {
                var sourceType = key.SourceType;
                var destType = key.DestType;

                sb.AppendLine($"【映射规则】{sourceType.Name} → {destType.Name}");

                // 输出自定义属性映射
                if (config._propertyMappings.Any())
                {
                    sb.AppendLine("  自定义属性映射：");
                    foreach (var (destProp, sourceProp) in config._propertyMappings)
                    {
                        sb.AppendLine($"    {destProp} ← {sourceProp}");
                    }
                }
                else
                {
                    sb.AppendLine("  自定义属性映射：无");
                }

                // 输出忽略的属性
                if (config._ignoredProperties.Any())
                {
                    sb.AppendLine("  忽略的属性：");
                    foreach (var ignoredProp in config._ignoredProperties)
                    {
                        sb.AppendLine($"    {ignoredProp}");
                    }
                }
                else
                {
                    sb.AppendLine("  忽略的属性：无");
                }
                sb.AppendLine();
            }

            return sb.ToString();
        }
    }
}
