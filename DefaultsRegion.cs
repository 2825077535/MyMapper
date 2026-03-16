using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
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

        public ConstructorInfo SelectBestConstructor(Type destinationType)
        {
            if (_constructorCache.TryGetValue(destinationType, out var cachedCtor))
                return cachedCtor;

            var ctors = destinationType.GetConstructors(BindingFlags.Public | BindingFlags.Instance);
            if (ctors.Length == 0)
                throw new MiniMapperException($"类型 {destinationType.Name} 无公共构造函数");

            var defaultCtor = ctors.FirstOrDefault(c => c.GetParameters().Length == 0);
            var bestCtor = defaultCtor ?? ctors.OrderByDescending(c => c.GetParameters().Length).First();

            _constructorCache[destinationType] = bestCtor;
            return bestCtor;
        }
    }
    /// <summary>
    /// 默认属性映射器
    /// </summary>
    public class DefaultPropertyMapper : IPropertyMapper
    {
        public void MapProperties<TSource, TDestination>(
            TSource source,
            TDestination destination,
            TypeMappingConfig? mappingConfig)
        {
            if (source == null || destination == null) return;

            var sourceType = typeof(TSource);
            var destType = typeof(TDestination);

            var destProperties = destType.GetProperties(BindingFlags.Public | BindingFlags.Instance);
            var sourceProperties = sourceType.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var destProp in destProperties)
            {
                var hasIgnoreAttribute = destProp.GetCustomAttribute<MapperIgnoreAttribute>() != null;
                var isConfigIgnored = mappingConfig?.IsIgnored(destProp.Name) ?? false;

                if (!destProp.CanWrite || hasIgnoreAttribute || isConfigIgnored)
                    continue;

                var sourcePropName = mappingConfig?.GetSourcePropertyName(destProp.Name) ?? destProp.Name;
                var sourceProp = sourceProperties.FirstOrDefault(p =>
                    string.Equals(p.Name, sourcePropName, StringComparison.OrdinalIgnoreCase) && p.CanRead);

                if (sourceProp == null) continue;

                try
                {
                    var sourceValue = sourceProp.GetValue(source);
                    var destValue = Convert.ChangeType(sourceValue, destProp.PropertyType);
                    destProp.SetValue(destination, destValue);
                }
                catch
                {
                    continue;
                }
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

        // 构造函数注入构造函数选择器（依赖注入）
        public DefaultExpressionCompiler(IConstructorSelector constructorSelector)
        {
            _constructorSelector = constructorSelector;
        }

        public Func<TSource, TDestination> Compile<TSource, TDestination>(TypeMappingConfig? mappingConfig)
        {
            var key = (typeof(TSource), typeof(TDestination));

            if (_compiledMaps.TryGetValue(key, out var existing))
                return (Func<TSource, TDestination>)existing;

            // 构建表达式树
            ParameterExpression sourceParam = Expression.Parameter(typeof(TSource), "source");
            ConstructorInfo ctor = _constructorSelector.SelectBestConstructor(typeof(TDestination));
            NewExpression newDest = Expression.New(ctor);

            List<MemberBinding> bindings = new List<MemberBinding>();
            foreach (var destProp in typeof(TDestination).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (!destProp.CanWrite) continue;
                bool hasIgnore = destProp.GetCustomAttribute<MapperIgnoreAttribute>() != null;
                if (hasIgnore) continue;
                bool isIgnored = mappingConfig?.IsIgnored(destProp.Name) ?? false;
                if (isIgnored) continue;

                string sourcePropName = mappingConfig?.GetSourcePropertyName(destProp.Name) ?? destProp.Name;
                var sourceProp = typeof(TSource).GetProperty(sourcePropName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (sourceProp == null) continue;

                MemberExpression sourcePropAccess = Expression.Property(sourceParam, sourceProp);
                if (sourceProp.PropertyType == destProp.PropertyType)
                {
                    bindings.Add(Expression.Bind(destProp, sourcePropAccess));
                }
            }

            MemberInitExpression init = Expression.MemberInit(newDest, bindings);
            Expression<Func<TSource, TDestination>> lambda =
                Expression.Lambda<Func<TSource, TDestination>>(init, sourceParam);
            Func<TSource, TDestination> func = lambda.Compile();

            _compiledMaps[key] = func;
            return func;
        }
    }
}
