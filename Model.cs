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
            // 1. 注册映射配置
            var mappingConfig = new MappingConfiguration();
            configure(mappingConfig);
            services.AddSingleton(mappingConfig);

            // 2. 注册策略接口
            services.AddSingleton<IConstructorSelector, DefaultConstructorSelector>();
            services.AddSingleton<IPropertyMapper, DefaultPropertyMapper>();
            services.AddSingleton<IExpressionCompiler, DefaultExpressionCompiler>();

            // 3. 注册核心映射器
            services.AddSingleton<IMapper, MiniMapper>();

            return services;
        }
    }
}
