using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyMapper
{
    /// <summary>
    /// 类型转换器注册表
    /// </summary>
    public class TypeConverterRegistry
    {
        private readonly Dictionary<(Type Source, Type Dest), object> _converters = new();

        /// <summary>
        /// 注册类型转换器
        /// </summary>
        public void Register<TSource, TDestination>(ITypeConverter<TSource, TDestination> converter)
        {
            _converters[(typeof(TSource), typeof(TDestination))] = converter;
        }

        /// <summary>
        /// 获取类型转换器
        /// </summary>
        public ITypeConverter<TSource, TDestination>? Get<TSource, TDestination>()
        {
            if (_converters.TryGetValue((typeof(TSource), typeof(TDestination)), out var converter))
            {
                return (ITypeConverter<TSource, TDestination>)converter;
            }
            return null;
        }
        /// <summary>
        /// 获取类型转换器（非泛型版，运行时确定类型）【新增修复】
        /// </summary>
        public object? Get(Type sourceType, Type destType)
        {
            if (_converters.TryGetValue((sourceType, destType), out var converter))
            {
                return converter;
            }
            return null;
        }
    }
}
