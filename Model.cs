using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
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
            Action<MappingConfiguration> configure,
            Action<TypeConverterRegistry>? configureConverters = null)
        {
            //配置映射规则
            var mappingConfig = new MappingConfiguration();
            configure(mappingConfig);

            // 配置类型转换器
            var converterRegistry = new TypeConverterRegistry();
            configureConverters?.Invoke(converterRegistry);

            // 注册核心服务（单例）
            services.AddSingleton(mappingConfig);
            services.AddSingleton(converterRegistry);
            services.AddSingleton<IConstructorSelector, DefaultConstructorSelector>();
            services.AddSingleton<IMapper, MiniMapper>();

            services.AddSingleton<IPropertyMapper>(provider =>
                new DefaultPropertyMapper(
                    provider.GetService<ILogger<DefaultPropertyMapper>>(),
                    provider.GetService<MappingConfiguration>(),
                    provider.GetService<TypeConverterRegistry>(),
                    provider.GetService<Lazy<IMapper>>() // 延迟解析，避免循环依赖
                ));
            services.AddSingleton<IExpressionCompiler, DefaultExpressionCompiler>();

            return services;
        }
    }

    /// <summary>用户状态枚举（转换器示例用）</summary>
    public enum UserStatus
    {
        Inactive = 0,
        Active = 1,
        Locked = 2
    }

    /// <summary>
    /// 常用转换器
    /// </summary>
    public static class BuiltInConverters
    {
        /// <summary>DateTime → string（yyyy-MM-dd）</summary>
        public class DateTimeToStringConverter : ITypeConverter<DateTime, string>
        {
            public string Convert(DateTime source, string? destination, TypeMappingConfig? mappingConfig)
            {
                return source.ToString("yyyy-MM-dd");
            }
        }

        /// <summary>int → UserStatus（枚举）</summary>
        public class IntToUserStatusConverter : ITypeConverter<int, UserStatus>
        {
            public UserStatus Convert(int source, UserStatus destination, TypeMappingConfig? mappingConfig)
            {
                return Enum.IsDefined(typeof(UserStatus), source)
                    ? (UserStatus)source
                    : UserStatus.Inactive;
            }
        }
    }
}