using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyMapper
{
    public static class TypeExtensions
    {
        /// <summary>
        /// 判断类型是否可空（引用类型/可空值类型）
        /// </summary>
        public static bool IsNullableType(this Type type)
        {
            if (!type.IsValueType) return true;
            return Nullable.GetUnderlyingType(type) != null;
        }
    }

    public static class MiniMapperDependencyInjection
    {
        public static IServiceCollection AddMiniMapper(this IServiceCollection services,
            Action<MappingConfiguration> configure)
        {
            var mappingConfig = new MappingConfiguration();
            configure(mappingConfig);
            services.AddSingleton(mappingConfig);
            services.AddSingleton<MiniMapper>();
            return services;
        }
    }
}
