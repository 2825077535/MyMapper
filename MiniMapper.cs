using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MyMapper
{
    public class MiniMapper:IMapper
    {
        private readonly MappingConfiguration _mappingConfig;
        private readonly IServiceProvider? _serviceProvider;
        private readonly IConstructorSelector _constructorSelector;
        private readonly IPropertyMapper _propertyMapper;
        private readonly IExpressionCompiler _expressionCompiler;
        private readonly ILogger<MiniMapper>? _logger;
        /// <summary>
        /// 用于存储编译后的映射委托，提升性能
        /// </summary>
        private readonly Dictionary<(Type Soure,Type Dest),Delegate> _compiledMaps = new();
        /// <summary>
        /// 构造函数注入所有依赖（核心：依赖注入）
        /// </summary>
        public MiniMapper(
            MappingConfiguration mappingConfig,
            IConstructorSelector constructorSelector,
            IPropertyMapper propertyMapper,
            IExpressionCompiler expressionCompiler,
            ILogger<MiniMapper>? logger=null,
            IServiceProvider? serviceProvider = null)
        {
            _mappingConfig = mappingConfig;
            _constructorSelector = constructorSelector;
            _propertyMapper = propertyMapper;
            _expressionCompiler = expressionCompiler;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        /// <summary>
        /// 核心映射方法
        /// </summary>
        public TDestination Map<TSource, TDestination>(TSource source)
        {
            if (source == null)
            {
                _logger?.LogWarning("映射源对象为 null：{SourceType}→{DestType}", typeof(TSource), typeof(TDestination));
                throw new ArgumentNullException(nameof(source), "源对象不能为 null");
            }
            var sourceType = typeof(TSource);
            var destType = typeof(TDestination);
            _logger?.LogDebug("开始映射：{SourceType}→{DestType}", sourceType.Name, destType.Name);
            try
            {
                _mappingConfig.TryGetMappingConfig(sourceType, destType, out var cfg);
                // 如果有条件映射 / 复杂集合映射等高级特性，则直接走反射
                if (cfg != null && cfg._propertyConditions.Count > 0)
                {
                    return FallbackMapByReflection<TSource, TDestination>(source);
                }
                var func = _expressionCompiler.Compile<TSource, TDestination>(cfg);
                var result = func(source);

                _logger?.LogInformation("映射成功（表达式树）：{SourceType}→{DestType}", sourceType.Name, destType.Name);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "表达式树映射失败，回退到反射：{SourceType}→{DestType}", sourceType.Name, destType.Name);
                return FallbackMapByReflection<TSource, TDestination>(source);
            }
        }

        /// <summary>增量映射（更新已有对象）</summary>
        public TDestination Map<TSource, TDestination>(TSource source, TDestination destination)
        {
            if (source == null)
            {
                _logger?.LogWarning("增量映射源对象为 null：{SourceType}→{DestType}", typeof(TSource), typeof(TDestination));
                throw new ArgumentNullException(nameof(source), "源对象不能为 null");
            }
            if (destination == null)
            {
                _logger?.LogWarning("增量映射目标对象为 null：{SourceType}→{DestType}", typeof(TSource), typeof(TDestination));
                throw new ArgumentNullException(nameof(destination), "目标对象不能为 null");
            }

            var sourceType = typeof(TSource);
            var destType = typeof(TDestination);
            _logger?.LogDebug("开始增量映射：{SourceType}→{DestType}", sourceType.Name, destType.Name);

            try
            {
                _mappingConfig.TryGetMappingConfig(sourceType, destType, out var cfg);
                _propertyMapper.MapProperties(source, destination, cfg);

                _logger?.LogInformation("增量映射成功：{SourceType}→{DestType}", sourceType.Name, destType.Name);
                return destination;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "增量映射失败：{SourceType}→{DestType}", sourceType.Name, destType.Name);
                throw new MappingExecutionException("增量映射失败", ex, sourceType, destType);
            }
        }

        /// <summary>集合映射（新建集合）</summary>
        public List<TDestination> Map<TSource, TDestination>(IEnumerable<TSource> source)
        {
            if (source == null)
            {
                _logger?.LogWarning("集合映射源为 null：{SourceType}→{DestType}", typeof(TSource), typeof(TDestination));
                throw new ArgumentNullException(nameof(source));
            }

            _logger?.LogInformation("开始集合映射：IEnumerable<{SourceType}>→List<{DestType}>", typeof(TSource).Name, typeof(TDestination).Name);

            var result = new List<TDestination>();
            foreach (var item in source)
            {
                if (item == null)
                {
                    _logger?.LogTrace("跳过null元素：{SourceType}", typeof(TSource).Name);
                    continue;
                }
                var destItem = Map<TSource, TDestination>(item);
                result.Add(destItem);
            }

            _logger?.LogInformation("集合映射完成：共映射 {Count} 个元素", result.Count);
            return result;
        }

        /// <summary>增量集合映射（更新已有集合）</summary>
        public void Map<TSource, TDestination>(IEnumerable<TSource> source, ICollection<TDestination> destination)
        {
            if (source == null)
            {
                _logger?.LogWarning("增量集合映射源为 null：{SourceType}→{DestType}", typeof(TSource), typeof(TDestination));
                throw new ArgumentNullException(nameof(source));
            }
            if (destination == null)
            {
                _logger?.LogWarning("增量集合映射目标为 null：{SourceType}→{DestType}", typeof(TSource), typeof(TDestination));
                throw new ArgumentNullException(nameof(destination));
            }

            _logger?.LogInformation("开始增量集合映射：IEnumerable<{SourceType}>→ICollection<{DestType}>", typeof(TSource).Name, typeof(TDestination).Name);

            destination.Clear(); // 清空原有数据
            foreach (var item in source)
            {
                if (item == null)
                {
                    _logger?.LogTrace("跳过null元素：{SourceType}", typeof(TSource).Name);
                    continue;
                }
                var destItem = Map<TSource, TDestination>(item);
                destination.Add(destItem);
            }

            _logger?.LogInformation("增量集合映射完成：共更新 {Count} 个元素", destination.Count);
        }

        /// <summary>
        /// 反射兜底逻辑
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <typeparam name="TDestination"></typeparam>
        /// <param name="source"></param>
        /// <returns></returns>
        /// <exception cref="MappingExecutionException"></exception>
        private TDestination FallbackMapByReflection<TSource, TDestination>(TSource source)
        {
            var sourceType = typeof(TSource);
            var destType = typeof(TDestination);
            _logger?.LogDebug("开始反射映射：{SourceType}→{DestType}", sourceType.Name, destType.Name);

            try
            {
                var destination = CreateDestinationInstance<TSource, TDestination>(source);
                _mappingConfig.TryGetMappingConfig(sourceType, destType, out var cfg);
                _propertyMapper.MapProperties(source, destination, cfg);

                _logger?.LogInformation("反射映射成功：{SourceType}→{DestType}", sourceType.Name, destType.Name);
                return destination;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "反射映射失败：{SourceType}→{DestType}", sourceType.Name, destType.Name);
                throw new MappingExecutionException("反射映射失败", ex, sourceType, destType);
            }
        }

        /// <summary>
        /// 创建目标实例
        /// </summary>
        private TDestination CreateDestinationInstance<TSource, TDestination>(TSource source)
        {
            var destType = typeof(TDestination);
            var sourceType = typeof(TSource);
            _logger?.LogDebug("创建目标实例：{DestType}", destType.Name);

            _mappingConfig.TryGetMappingConfig(sourceType, destType, out var typeConfig);

            var ctor = _constructorSelector.SelectBestConstructor(destType);
            var parameters = ctor.GetParameters();
            var paramValues = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                object? paramValue = null;

                try
                {
                    // 从映射配置获取参数值
                    if (typeConfig != null)
                    {
                        paramValue = typeConfig.GetCtorParamValue(param.Name!, source);
                    }

                    // 从DI容器获取参数值
                    if (paramValue == null && _serviceProvider != null)
                    {
                        paramValue = _serviceProvider.GetService(param.ParameterType);
                    }

                    // 使用默认值
                    if (paramValue == null && param.HasDefaultValue)
                    {
                        paramValue = param.DefaultValue;
                    }

                    // 检查必填参数
                    if (paramValue == null && !param.ParameterType.IsNullableType())
                    {
                        throw new MappingExecutionException($"无法解析构造参数 {param.Name}（类型：{param.ParameterType.Name}）",
                            null, sourceType, destType);
                    }

                    paramValues[i] = paramValue;
                    _logger?.LogTrace("构造参数赋值：{ParamName} = {ParamValue}", param.Name, paramValue ?? "null");
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "构造参数解析失败：{ParamName}", param.Name);
                    throw;
                }
            }

            try
            {
                return (TDestination)ctor.Invoke(paramValues);
            }
            catch (TargetInvocationException ex)
            {
                throw new MappingExecutionException("创建目标实例失败", ex.InnerException, sourceType, destType);
            }
        }
    }
}
