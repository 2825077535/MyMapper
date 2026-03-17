using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyMapper
{
    public class PropertyMapCondition
    {
        /// <summary>
        /// 条件映射委托
        /// </summary>
        /// <typeparam name="TSource">源类型</typeparam>
        /// <typeparam name="TDestination">目标类型</typeparam>
        /// <param name="source">源对象</param>
        /// <param name="destination">目标对象（增量映射时不为null）</param>
        /// <param name="sourceValue">源属性值</param>
        /// <returns>是否满足映射条件</returns>
        public delegate bool PropertyMapConditionDelegate<TSource, TDestination>(TSource source, TDestination? destination, object? sourceValue);


    }
}
