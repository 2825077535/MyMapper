using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MyMapper
{
    /// <summary>
    /// 映射器核心接口
    /// </summary>
    public interface IMapper
    {
        /// <summary>基础映射（新建对象）</summary>
        TDestination Map<TSource, TDestination>(TSource source);

        /// <summary>增量映射（更新已有对象）</summary>
        TDestination Map<TSource, TDestination>(TSource source, TDestination destination);

        /// <summary>集合映射（新建集合）</summary>
        List<TDestination> Map<TSource, TDestination>(IEnumerable<TSource> source);

        /// <summary>增量集合映射（更新已有集合）</summary>
        void Map<TSource, TDestination>(IEnumerable<TSource> source, ICollection<TDestination> destination);
    }
    /// <summary>
    /// 构造函数选择策略接口
    /// </summary>
    public interface IConstructorSelector
    {
        ConstructorInfo SelectBestConstructor(Type destinationType);
    }
    /// <summary>
    /// 属性映射策略接口
    /// </summary>
    public interface IPropertyMapper
    {
        void MapProperties<TSource, TDestination>(
        TSource source,
        TDestination destination,
            TypeMappingConfig? mappingConfig);
    }
    /// <summary>
    /// 表达式树编译接口
    /// </summary>
    public interface IExpressionCompiler
    {
        Func<TSource, TDestination> Compile<TSource, TDestination>(TypeMappingConfig? mappingConfig);
    }
    /// <summary>
    /// 自定义类型转换器接口
    /// </summary>
    /// <typeparam name="TSource">源类型</typeparam>
    /// <typeparam name="TDestination">目标类型</typeparam>
    public interface ITypeConverter<TSource, TDestination>
    {
        /// <summary>
        /// 类型转换
        /// </summary>
        TDestination Convert(TSource source, TDestination? destination, TypeMappingConfig? mappingConfig);
    }
}
