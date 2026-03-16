using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace MyMapper
{
    /// <summary>
    /// 映射器接口
    /// </summary>
    public interface IMapper
    {
        TDestination Map<TSource, TDestination>(TSource source);
        TDestination Map<TSource, TDestination>(TSource source, TDestination destination);
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
}
