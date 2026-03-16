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
            IServiceProvider? serviceProvider = null)
        {
            _mappingConfig = mappingConfig;
            _constructorSelector = constructorSelector;
            _propertyMapper = propertyMapper;
            _expressionCompiler = expressionCompiler;
            _serviceProvider = serviceProvider;
        }

        /// <summary>
        /// 核心映射方法
        /// </summary>
        public TDestination Map<TSource, TDestination>(TSource source)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));

            try
            {
                _mappingConfig.TryGetMappingConfig(typeof(TSource), typeof(TDestination), out var cfg);
                var func = _expressionCompiler.Compile<TSource, TDestination>(cfg);
                return func(source);
            }
            catch
            {
                return FallbackMapByReflection<TSource, TDestination>(source);
            }
        }

        /// <summary>
        /// 增量映射结果，只更新字段，不创建新对象
        /// </summary>
        public TDestination Map<TSource, TDestination>(TSource source, TDestination destination)
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            if (destination == null)
                throw new ArgumentNullException(nameof(destination));

            _mappingConfig.TryGetMappingConfig(typeof(TSource), typeof(TDestination), out var cfg);
            _propertyMapper.MapProperties(source, destination, cfg);
            return destination;
        }

        /// <summary>
        /// 反射兜底逻辑
        /// </summary>
        private TDestination FallbackMapByReflection<TSource, TDestination>(TSource source)
        {
            var destination = CreateDestinationInstance<TSource, TDestination>(source);
            _mappingConfig.TryGetMappingConfig(typeof(TSource), typeof(TDestination), out var cfg);
            _propertyMapper.MapProperties(source, destination, cfg);
            return destination;
        }

        /// <summary>
        /// 创建目标实例
        /// </summary>
        private TDestination CreateDestinationInstance<TSource, TDestination>(TSource source)
        {
            var destType = typeof(TDestination);
            var sourceType = typeof(TSource);

            _mappingConfig.TryGetMappingConfig(sourceType, destType, out var typeConfig);

            var ctor = _constructorSelector.SelectBestConstructor(destType);
            var parameters = ctor.GetParameters();
            var paramValues = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                var param = parameters[i];
                object? paramValue = null;

                if (typeConfig != null)
                {
                    paramValue = typeConfig.GetCtorParamValue(param.Name!, source);
                }

                if (paramValue == null && _serviceProvider != null)
                {
                    paramValue = _serviceProvider.GetService(param.ParameterType);
                }

                if (paramValue == null && param.HasDefaultValue)
                {
                    paramValue = param.DefaultValue;
                }

                if (paramValue == null && !param.ParameterType.IsNullableType())
                {
                    throw new MiniMapperException($"无法解析构造参数 {param.Name}（类型：{param.ParameterType}）");
                }

                paramValues[i] = paramValue;
            }

            try
            {
                return (TDestination)ctor.Invoke(paramValues);
            }
            catch (TargetInvocationException ex)
            {
                throw new MiniMapperException("创建目标实例失败", ex.InnerException);
            }
        }
    }
}
