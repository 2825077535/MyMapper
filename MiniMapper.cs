using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyMapper
{
    public static class MiniMapper
    {
        /// <summary>
        /// 基础的对象映射方法，支持同名同类型属性的映射，并且可以通过MapperIgnoreAttribute特性来忽略某些属性的映射。
        /// </summary>
        /// <typeparam name="TSource">源类型</typeparam>
        /// <typeparam name="TDestination">目标类型</typeparam>
        /// <param name="source"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentNullException"></exception>
        public static TDestination Map<TSource, TDestination>(TSource source)
            where TDestination : new()
            // 约束目标类型必须有无参构造函数，由于外部工具并不知道构造函数的信息内容，
            // 如果不约束无参构造，那么需要另外定义具体的传参构造。
            // 对于外部工具来说，由于对构造的信息不了解，所以只能约束目标类型必须有无参构造函数，这样才能通过反射创建目标对象的实例。
        {
            if (source == null)
                throw new ArgumentNullException(nameof(source));
            TDestination destination = new TDestination();
            var sourceProperties = typeof(TSource).GetProperties();// 获取源类型的属性列表
            var destinationProperties = typeof(TDestination).GetProperties();// 获取目标类型的属性列表
            foreach (var destProp in destinationProperties)
            {
                // 如果目标属性上有MapperIgnoreAttribute特性，则跳过该属性的映射
                if (destProp.GetCustomAttributes(typeof(MapperIgnoreAttribute), true).Length > 0)
                    continue;
                var sourceProp = sourceProperties.FirstOrDefault(sp => sp.Name == destProp.Name && sp.PropertyType == destProp.PropertyType);
                if (sourceProp != null)
                {
                    var value = sourceProp.GetValue(source);
                    destProp.SetValue(destination, value);
                }
            }
            return destination;
        }
    }
}
