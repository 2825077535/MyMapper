using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
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

        /// <summary>
        /// 用于存储编译后的映射委托，提升性能
        /// </summary>
        private readonly Dictionary<(Type Soure,Type Dest),Delegate> _compiledMaps = new();
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

            try
            {
                var func = CompileMapper<TSource, TDestination>();
                return func(source);
            }
            catch
            {
                // 编译失败回退到原有反射逻辑
                return FallbackMapByReflection<TSource, TDestination>(source);
            }
        }
        /// <summary>
        /// 创建并编译映射表达式，生成高性能映射委托
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <typeparam name="TDestination"></typeparam>
        /// <returns></returns>
        private Func<TSource, TDestination> CompileMapper<TSource, TDestination>()
        {
            var key = (typeof(TSource), typeof(TDestination));

            // 缓存命中直接返回
            if (_compiledMaps.TryGetValue(key, out var existing))
                return (Func<TSource, TDestination>)existing;

            // 获取映射配置
            _mappingConfig.TryGetMappingConfig(typeof(TSource), typeof(TDestination), out var cfg);

            // 构建表达式参数：source
            ParameterExpression sourceParam = Expression.Parameter(typeof(TSource), "source");

            // 创建目标对象（无参构造）
            ConstructorInfo ctor = GetBestConstructor(typeof(TDestination));
            NewExpression newDest = Expression.New(ctor);

            // 收集属性绑定
            List<MemberBinding> bindings = new List<MemberBinding>();
            foreach (var destProp in typeof(TDestination).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                // 跳过不可写、特性忽略、配置忽略的属性
                if (!destProp.CanWrite) continue;
                bool hasIgnoreAttr = destProp.GetCustomAttribute<MapperIgnoreAttribute>() != null;
                if (hasIgnoreAttr) continue;
                bool isConfigIgnored = cfg?.IsIgnored(destProp.Name) ?? false;
                if (isConfigIgnored) continue;

                // 找源属性（自定义规则→默认同名）
                string sourcePropName = cfg?.GetSourcePropertyName(destProp.Name) ?? destProp.Name;
                var sourceProp = typeof(TSource).GetProperty(sourcePropName,
                    BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase);
                if (sourceProp == null) continue;

                // 构建属性访问表达式
                MemberExpression sourcePropAccess = Expression.Property(sourceParam, sourceProp);

                // 类型匹配则添加绑定
                if (sourceProp.PropertyType == destProp.PropertyType)
                {
                    bindings.Add(Expression.Bind(destProp, sourcePropAccess));
                }
            }

            // 构建对象初始化表达式
            MemberInitExpression init = Expression.MemberInit(newDest, bindings);

            // 编译为委托并缓存
            Expression<Func<TSource, TDestination>> lambda =
                Expression.Lambda<Func<TSource, TDestination>>(init, sourceParam);
            Func<TSource, TDestination> func = lambda.Compile();
            _compiledMaps[key] = func;

            return func;
        }
        /// <summary>
        /// 如果编译失败，回退到原有反射实现
        /// </summary>
        /// <typeparam name="TSource"></typeparam>
        /// <typeparam name="TDestination"></typeparam>
        /// <param name="source"></param>
        /// <returns></returns>
        private TDestination FallbackMapByReflection<TSource, TDestination>(TSource source)
        {
            var sourceType = typeof(TSource);
            var destType = typeof(TDestination);

            var destination = CreateDestinationInstance<TSource, TDestination>(source);
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
