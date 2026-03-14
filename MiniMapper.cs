using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MyMapper
{
    public class MiniMapper
    {
        private readonly MappingConfiguration _mappingConfig;
        private readonly IServiceProvider? _serviceProvider;
        private readonly Dictionary<Type, ConstructorInfo> _ctorCache = new();

        public MiniMapper(MappingConfiguration mappingConfig, IServiceProvider? serviceProvider = null)
        {
            _mappingConfig = mappingConfig;
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// 核心映射方法（有规则用规则，无规则默认匹配）
        /// </summary>
        public TDestination Map<TSource, TDestination>(TSource source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            var sourceType = typeof(TSource);
            var destType = typeof(TDestination);

            // 创建目标实例（支持有参/无参构造，自动匹配参数）
            var destination = CreateDestinationInstance<TSource, TDestination>(source);

            // 执行属性映射（有规则用规则，无则默认同名匹配）
            MapProperties(source, destination);

            return destination;
        }

        /// <summary>
        /// 创建目标实例（无参构造优先，有参构造自动匹配参数名）
        /// </summary>
        private TDestination CreateDestinationInstance<TSource, TDestination>(TSource source)
        {
            var destType = typeof(TDestination);
            var sourceType = typeof(TSource);

            // 获取映射配置（无配置则默认匹配）
            _mappingConfig.TryGetMappingConfig(sourceType, destType, out var typeConfig);

            // 选择最佳构造函数（无参优先）
            var ctor = GetBestConstructor(destType);
            var parameters = ctor.GetParameters();
            var paramValues = new object?[parameters.Length];

            // 解析构造参数（有规则用规则，无则默认匹配参数名=属性名）
            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                object? paramValue = null;

                // 优先用自定义映射规则
                if (typeConfig != null)
                {
                    paramValue = typeConfig.GetCtorParamValue(param.Name!, source);
                }

                // 无规则则尝试从DI容器解析（服务依赖）
                if (paramValue == null && _serviceProvider != null)
                {
                    paramValue = _serviceProvider.GetService(param.ParameterType);
                }

                // 仍无值则用参数默认值
                if (paramValue == null && param.HasDefaultValue)
                {
                    paramValue = param.DefaultValue;
                }

                // 最终无值且参数不可空则抛异常
                if (paramValue == null && !param.ParameterType.IsNullableType())
                {
                    throw new MiniMapperException(
                        $"无法解析构造参数 {param.Name}（类型：{param.ParameterType}），" +
                        $"请确保参数名与源属性名匹配，或配置构造参数映射规则");
                }

                paramValues[i] = paramValue;
            }

            // 调用构造函数创建实例
            try
            {
                return (TDestination)ctor.Invoke(paramValues);
            }
            catch (TargetInvocationException ex)
            {
                throw new MiniMapperException("调用目标类型构造函数失败", ex.InnerException ?? ex);
            }
        }

        /// <summary>
        /// 执行属性映射（有规则用规则，无则默认同名匹配）
        /// </summary>
        private void MapProperties<TSource, TDestination>(TSource source, TDestination destination)
        {
            if (source == null || destination == null) return;

            var sourceType = typeof(TSource);
            var destType = typeof(TDestination);

            // 获取映射配置（无配置则默认属性名匹配）
            _mappingConfig.TryGetMappingConfig(sourceType, destType, out var typeConfig);

            // 获取属性列表
            var destProperties = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var sourceProperties = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var destProp in destProperties)
            {
                // 检测MapperIgnoreAttribute特性
                // 忽略逻辑优先级：特性标记 > 配置式忽略 > 不可写属性
                var hasIgnoreAttribute = destProp.GetCustomAttribute<MapperIgnoreAttribute>() != null;
                var isConfigIgnored = typeConfig?.IsIgnored(destProp.Name) ?? false;

                if (!destProp.CanWrite || hasIgnoreAttribute || isConfigIgnored)
                    continue;

                // 获取源属性名（有规则用规则，无则默认同名）
                var sourcePropName = typeConfig?.GetSourcePropertyName(destProp.Name) ?? destProp.Name;

                // 匹配源属性（忽略大小写）
                var sourceProp = sourceProperties.FirstOrDefault(p =>
                    string.Equals(p.Name, sourcePropName, StringComparison.OrdinalIgnoreCase) && p.CanRead);

                if (sourceProp == null) continue;

                // 类型兼容则赋值（支持隐式转换，如int→long）
                try
                {
                    var sourceValue = sourceProp.GetValue(source);
                    var destValue = Convert.ChangeType(sourceValue, destProp.PropertyType);
                    destProp.SetValue(destination, destValue);
                }
                catch (InvalidCastException)
                {
                    // 类型不兼容则跳过
                    continue;
                }
                catch (ArgumentNullException)
                {
                    // 源值为null且目标类型不可空则跳过
                    if (!destProp.PropertyType.IsNullableType()) continue;
                    destProp.SetValue(destination, null);
                }
            }
        }
        /// <summary>
        /// 选择最佳构造函数（无参优先，无则选参数最多的）
        /// </summary>
        private ConstructorInfo GetBestConstructor(Type destType)
        {
            if (_ctorCache.TryGetValue(destType, out var cachedCtor))
                return cachedCtor;

            var ctors = destType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (ctors.Length == 0)
                throw new MiniMapperException($"类型 {destType.Name} 没有公共构造函数");

            // 优先无参构造，无则选参数最多的
            var defaultCtor = ctors.FirstOrDefault(c => c.GetParameters().Length == 0);
            var bestCtor = defaultCtor ?? ctors.OrderByDescending(c => c.GetParameters().Length).First();

            _ctorCache[destType] = bestCtor;
            return bestCtor;
        }
    }
}
