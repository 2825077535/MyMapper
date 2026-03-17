using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Security.AccessControl;
using System.Text;
using System.Threading.Tasks;

namespace MyMapper
{
    /// <summary>
    /// 默认构造函数选择器
    /// </summary>
    public class DefaultConstructorSelector : IConstructorSelector
    {
        private readonly Dictionary<Type, ConstructorInfo> _constructorCache = new();
        private readonly ILogger<DefaultConstructorSelector>? _logger;
        public DefaultConstructorSelector(ILogger<DefaultConstructorSelector>? logger = null)
        {
            _logger = logger;
        }
        public ConstructorInfo SelectBestConstructor(Type destinationType)
        {
            if (_constructorCache.TryGetValue(destinationType, out var cachedCtor))
            {
                _logger?.LogDebug("构造函数缓存命中：{DestType}", destinationType.Name);
                return cachedCtor;
            }
            var ctors = destinationType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (ctors.Length == 0)
            {
                var ex = new MappingConfigException($"类型 {destinationType.Name} 无公共构造函数", null, destinationType);
                _logger?.LogError(ex, "构造函数选择失败：{DestType}", destinationType.Name);
                throw ex;
            }
            var defaultCtor = ctors.FirstOrDefault(c => c.GetParameters().Length == 0);
            var bestCtor = defaultCtor ?? ctors.OrderByDescending(c => c.GetParameters().Length).First();

            _constructorCache[destinationType] = bestCtor;
            _logger?.LogInformation("构造函数选择成功：{DestType} → {CtorParams}个参数",
            destinationType.Name, bestCtor.GetParameters().Length);
            return bestCtor;
        }
    }
    /// <summary>
    /// 默认属性映射器
    /// </summary>
    public class DefaultPropertyMapper : IPropertyMapper
    {
        private readonly ILogger<DefaultPropertyMapper>? _logger;
        private readonly MappingConfiguration _mappingConfig;
        private readonly TypeConverterRegistry _typeConverterRegistry;
        private readonly Lazy<IMapper>? _lazyMapper;

        public DefaultPropertyMapper(
            ILogger<DefaultPropertyMapper>? logger = null,
            MappingConfiguration mappingConfig = null,
            TypeConverterRegistry typeConverterRegistry = null,
            Lazy<IMapper>? lazyMapper = null)
        {
            _logger = logger;
            _mappingConfig = mappingConfig ?? new MappingConfiguration();
            _typeConverterRegistry = typeConverterRegistry ?? new TypeConverterRegistry();
            _lazyMapper = lazyMapper;
        }

        public void MapProperties<TSource, TDestination>(
            TSource source,
            TDestination destination,
            TypeMappingConfig? mappingConfig,
            int currentDepth = 0)
        {
            if (source == null || destination == null)
            {
                _logger?.LogWarning("源/目标对象为 null：{SourceType}→{DestType}", typeof(TSource), typeof(TDestination));
                return;
            }
            var sourceType = typeof(TSource);
            var destType = typeof(TDestination);
            _logger?.LogDebug("开始属性映射：{SourceType}→{DestType}（深度：{Depth}）",
                sourceType.Name, destType.Name, currentDepth);

            var destProperties = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var sourceProperties = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var destProp in destProperties)
            {
                try
                {
                    //跳过索引器属性（如集合的 Item[int index]），避免 TargetParameterCountException
                    if (destProp.GetIndexParameters().Length > 0)
                    {
                        _logger?.LogTrace("跳过索引器属性：{DestProp}", destProp.Name);
                        continue;
                    }

                    // 基础忽略逻辑
                    var hasIgnoreAttribute = destProp.GetCustomAttribute<MapperIgnoreAttribute>() != null;
                    var isConfigIgnored = mappingConfig?.IsIgnored(destProp.Name) ?? false;

                    if (!destProp.CanWrite || hasIgnoreAttribute || isConfigIgnored)
                    {
                        _logger?.LogTrace("跳过属性：{DestProp}（不可写/忽略）", destProp.Name);
                        continue;
                    }

                    // 获取源属性名称
                    var sourcePropName = mappingConfig?.GetSourcePropertyName(destProp.Name) ?? destProp.Name;
                    var sourceProp = sourceProperties.FirstOrDefault(p =>
                        string.Equals(p.Name, sourcePropName, StringComparison.OrdinalIgnoreCase) && p.CanRead);

                    if (sourceProp == null)
                    {
                        _logger?.LogWarning("源属性不存在：{SourceProp}（目标属性：{DestProp}）", sourcePropName, destProp.Name);
                        continue;
                    }

                    // 获取源属性值
                    var sourceValue = sourceProp.GetValue(source);

                    // 条件映射检查
                    if (mappingConfig != null && !mappingConfig.ShouldMapProperty(destProp.Name, source, destination, sourceValue))
                    {
                        _logger?.LogTrace("跳过属性：{DestProp}（条件不满足）", destProp.Name);
                        continue;
                    }

                    if (sourceValue == null)
                    {
                        _logger?.LogTrace("跳过属性：{DestProp}（源值为null）", destProp.Name);
                        continue;
                    }

                    // 自定义类型转换 + 递归映射
                    object? destValue = null;
                    var sourceValueType = sourceValue.GetType();
                    var destPropType = destProp.PropertyType;

                    // 优先使用自定义转换器
                    var converter = _typeConverterRegistry.Get(sourceValueType, destPropType);
                    if (converter != null)
                    {
                        var convertMethod = converter.GetType().GetMethod("Convert", new[] { sourceValueType, destPropType, typeof(TypeMappingConfig) });
                        if (convertMethod != null)
                        {
                            var currentDestValue = SafeGetPropertyValue(destProp, destination!);
                            destValue = convertMethod.Invoke(converter, new[] { sourceValue, currentDestValue, mappingConfig });
                            _logger?.LogTrace("使用自定义转换器：{SourceType}→{DestType}", sourceValueType.Name, destPropType.Name);
                        }
                    }
                    // 递归映射集合和复杂对象
                    else if (IsComplexType(sourceValueType) && _lazyMapper != null)
                    {
                        var currentDestValue = SafeGetPropertyValue(destProp, destination!);
                        var mapper = _lazyMapper.Value;

                        // 处理集合类型（IEnumerable）
                        if (typeof(System.Collections.IEnumerable).IsAssignableFrom(sourceValueType) && sourceValueType != typeof(string))
                        {
                            var sourceEnumerable = sourceValue as System.Collections.IEnumerable;
                            if (sourceEnumerable != null)
                            {
                                Type destItemType = destPropType.IsGenericType
                                    ? destPropType.GetGenericArguments()[0]
                                    : destPropType.GetElementType() ?? destPropType;

                                Type sourceItemType = sourceValueType.IsGenericType
                                    ? sourceValueType.GetGenericArguments()[0]
                                    : sourceValueType.GetElementType() ?? sourceValueType;

                                Type listType = typeof(List<>).MakeGenericType(destItemType);
                                System.Collections.IList destList = (System.Collections.IList)Activator.CreateInstance(listType)!;

                                foreach (var sourceItem in sourceEnumerable)
                                {
                                    if (sourceItem == null) continue;

                                    var mapMethod = typeof(IMapper).GetMethod("Map")!
                                        .MakeGenericMethod(sourceItemType, destItemType);
                                    var destItem = mapMethod.Invoke(mapper, new[] { sourceItem });

                                    if (destItem != null)
                                    {
                                        destList.Add(destItem);
                                    }
                                }

                                destValue = destList;
                                _logger?.LogTrace("集合映射完成：{SourceType}→{DestType}，共{Count}个元素",
                                    sourceValueType.Name, destPropType.Name, destList.Count);
                            }
                        }
                        // 处理单个复杂对象
                        else
                        {
                            if (currentDestValue == null)
                            {
                                currentDestValue = Activator.CreateInstance(destPropType);
                            }
                            var genericMapMethod = typeof(IMapper).GetMethod("Map", new[] { sourceValueType, destPropType });
                            destValue = genericMapMethod.Invoke(mapper, new[] { sourceValue, currentDestValue });
                            _logger?.LogTrace("递归映射复杂类型：{SourceType}→{DestType}（深度：{NewDepth}）",
                                sourceValueType.Name, destPropType.Name, currentDepth + 1);
                        }
                    }
                    // 默认类型转换
                    else
                    {
                        destValue = Convert.ChangeType(sourceValue, destPropType);
                        _logger?.LogTrace("使用默认转换：{SourceType}→{DestType}", sourceValueType.Name, destPropType.Name);
                    }

                    // 赋值到目标对象
                    destProp.SetValue(destination, destValue);
                    _logger?.LogTrace("属性映射成功：{DestProp} ← {SourceProp}", destProp.Name, sourceProp.Name);
                }
                catch (Exception ex)
                {
                    _logger?.LogError(ex, "属性映射失败：{DestProp}", destProp.Name);
                    throw new MappingExecutionException($"属性 {destProp.Name} 映射失败", ex, sourceType, destType, destProp.Name);
                }
            }

            _logger?.LogDebug("属性映射完成：{SourceType}→{DestType}", sourceType.Name, destType.Name);
        }

        /// <summary>
        /// 安全获取属性值：跳过索引器属性，避免 TargetParameterCountException
        /// </summary>
        private static object? SafeGetPropertyValue(PropertyInfo propertyInfo, object target)
        {
            if (propertyInfo.GetIndexParameters().Length > 0)
            {
                return null;
            }
            return propertyInfo.GetValue(target);
        }

        /// <summary>仅排除值类型、字符串、数组，不排除IEnumerable（集合）</summary>
        private bool IsComplexType(Type type)
        {
            return !type.IsValueType && type != typeof(string) && !type.IsArray;
        }
    }

    /// <summary>
    /// 默认表达式树编译器
    /// </summary>
    public class DefaultExpressionCompiler : IExpressionCompiler
    {
        private readonly Dictionary<(Type Source, Type Dest), Delegate> _compiledMaps = new();
        private readonly IConstructorSelector _constructorSelector;
        private readonly ILogger<DefaultExpressionCompiler>? _logger;
        public DefaultExpressionCompiler(
        IConstructorSelector constructorSelector,
        ILogger<DefaultExpressionCompiler>? logger = null)
        {
            _constructorSelector = constructorSelector;
            _logger = logger;
        }

        public Func<TSource, TDestination> Compile<TSource, TDestination>(TypeMappingConfig? mappingConfig)
        {
            var key = (typeof(TSource), typeof(TDestination));
            var sourceType = typeof(TSource);
            var destType = typeof(TDestination);
            // 缓存命中
            if (_compiledMaps.TryGetValue(key, out var existing))
            {
                _logger?.LogDebug("表达式树缓存命中：{SourceType}→{DestType}", sourceType.Name, destType.Name);
                return (Func<TSource, TDestination>)existing;
            }

            _logger?.LogInformation("开始编译表达式树：{SourceType}→{DestType}", sourceType.Name, destType.Name);

            try
            {
                ParameterExpression sourceParam = Expression.Parameter(sourceType, "source");

                ConstructorInfo ctor = _constructorSelector.SelectBestConstructor(destType);
                NewExpression newDest = Expression.New(ctor);

                List<MemberBinding> bindings = new List<MemberBinding>();
                foreach (var destProp in destType.GetProperties(BindingFlags.Public | BindingFlags.Instance))
                {
                    if (!destProp.CanWrite) continue;

                    bool hasIgnore = destProp.GetCustomAttribute<MapperIgnoreAttribute>() != null;
                    if (hasIgnore) continue;

                    bool isIgnored = mappingConfig?.IsIgnored(destProp.Name) ?? false;
                    if (isIgnored) continue;

                    string sourcePropName = mappingConfig?.GetSourcePropertyName(destProp.Name) ?? destProp.Name;
                    var sourceProp = sourceType.GetProperty(sourcePropName,
                        BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                    if (sourceProp == null) continue;

                    if (sourceProp.PropertyType == destProp.PropertyType)
                    {
                        MemberExpression sourcePropAccess = Expression.Property(sourceParam, sourceProp);

                        Expression? conditionExpr = null;
                        if (mappingConfig != null && mappingConfig._propertyConditions.ContainsKey(destProp.Name))
                        {
                            conditionExpr = Expression.NotEqual(
                                sourcePropAccess,
                                Expression.Constant(null, sourceProp.PropertyType)
                            );
                        }

                        bindings.Add(Expression.Bind(destProp, sourcePropAccess));
                        _logger?.LogTrace("添加表达式树绑定：{DestProp} ← {SourceProp}", destProp.Name, sourceProp.Name);
                    }
                }

                // 构建对象初始化表达式
                MemberInitExpression init = Expression.MemberInit(newDest, bindings);

                // 编译为委托
                Expression<Func<TSource, TDestination>> lambda =
                    Expression.Lambda<Func<TSource, TDestination>>(init, sourceParam);
                Func<TSource, TDestination> func = lambda.Compile();

                // 缓存编译结果
                _compiledMaps[key] = func;
                _logger?.LogInformation("表达式树编译成功：{SourceType}→{DestType}（绑定{BindingCount}个属性）",
                    sourceType.Name, destType.Name, bindings.Count);

                return func;
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "表达式树编译失败：{SourceType}→{DestType}", sourceType.Name, destType.Name);
                throw new MappingExecutionException("表达式树编译失败", ex, sourceType, destType);
            }
        }
    }
}