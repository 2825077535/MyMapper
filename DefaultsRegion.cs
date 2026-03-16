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
        public DefaultPropertyMapper(ILogger<DefaultPropertyMapper>? logger = null)
        {
            _logger = logger;
        }
        public void MapProperties<TSource, TDestination>(
            TSource source,
            TDestination destination,
            TypeMappingConfig? mappingConfig)
        {
            if (source == null || destination == null)
            {
                _logger?.LogWarning("源/目标对象为 null：{SourceType}→{DestType}", typeof(TSource), typeof(TDestination));
                return;
            }
            var sourceType = typeof(TSource);
            var destType = typeof(TDestination);
            _logger?.LogDebug("开始属性映射：{SourceType}→{DestType}", sourceType.Name, destType.Name);
            var destProperties = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var sourceProperties = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var destProp in destProperties)
            {
                var hasIgnoreAttribute = destProp.GetCustomAttribute<MapperIgnoreAttribute>() != null;
                var isConfigIgnored = mappingConfig?.IsIgnored(destProp.Name) ?? false;

                if (!destProp.CanWrite || hasIgnoreAttribute || isConfigIgnored)
                {
                    _logger?.LogTrace("跳过属性：{DestProp}（不可写/忽略）", destProp.Name);
                    continue;
                }

                var sourcePropName = mappingConfig?.GetSourcePropertyName(destProp.Name) ?? destProp.Name;
                var sourceProp = sourceProperties.FirstOrDefault(p =>
                    string.Equals(p.Name, sourcePropName, StringComparison.OrdinalIgnoreCase) && p.CanRead);

                if (sourceProp == null)
                {
                    _logger?.LogWarning("源属性不存在：{SourceProp}（目标属性：{DestProp}）", sourcePropName, destProp.Name);
                    continue;
                }
                try
                {
                    var sourceValue = sourceProp.GetValue(source);
                    var destValue = Convert.ChangeType(sourceValue, destProp.PropertyType);
                    destProp.SetValue(destination, destValue);
                }
                catch(Exception ex)
                {
                    _logger?.LogError(ex, "属性映射失败：{DestProp}", destProp.Name);
                    throw new MappingExecutionException($"属性 {destProp.Name} 映射失败", ex, sourceType, destType, destProp.Name);
                }
                _logger?.LogTrace("属性映射成功：{DestProp} ← {SourceProp}", destProp.Name, sourceProp.Name);
            }
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
        // 构造函数注入构造函数选择器（依赖注入）
        public DefaultExpressionCompiler(
        IConstructorSelector constructorSelector,
        ILogger<DefaultExpressionCompiler>? logger=null)
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
            try
            {
                // 构建表达式树参数
                ParameterExpression sourceParam = Expression.Parameter(sourceType, "source");

                // 选择构造函数
                ConstructorInfo ctor = _constructorSelector.SelectBestConstructor(destType);
                NewExpression newDest = Expression.New(ctor);

                // 构建属性绑定
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

                    // 仅处理类型完全匹配的属性
                    if (sourceProp.PropertyType == destProp.PropertyType)
                    {
                        MemberExpression sourcePropAccess = Expression.Property(sourceParam, sourceProp);
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
