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
                // 优先使用表达式树编译的委托
                _mappingConfig.TryGetMappingConfig(sourceType, destType, out var cfg);
                var func = _expressionCompiler.Compile<TSource, TDestination>(cfg);
                var result = func(source);

                _logger?.LogInformation("映射成功（表达式树）：{SourceType}→{DestType}", sourceType.Name, destType.Name);
                return result;
            }
            catch (Exception ex)
            {
                _logger?.LogWarning(ex, "表达式树映射失败，回退到反射：{SourceType}→{DestType}", sourceType.Name, destType.Name);
                // 回退到反射逻辑
                return FallbackMapByReflection<TSource, TDestination>(source);
            }
        }

        /// <summary>
        /// 增量映射结果，只更新字段，不创建新对象
        /// </summary>
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

        /// <summary>
        /// 反射兜底逻辑
        /// </summary>
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
