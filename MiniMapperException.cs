using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MyMapper
{
    public class MiniMapperException : Exception
    {
        public MiniMapperException(string message) : base(message) { }
        public MiniMapperException(string message, Exception innerException) : base(message, innerException) { }
    }

    /// <summary>
    /// 映射配置异常（初始化/配置阶段）
    /// </summary>
    public class MappingConfigException : MiniMapperException
    {
        public Type? SourceType { get; }
        public Type? DestType { get; }

        public MappingConfigException(string message, Type? sourceType = null, Type? destType = null)
            : base(message)
        {
            SourceType = sourceType;
            DestType = destType;
        }
    }

    /// <summary>
    /// 映射执行异常（运行时）
    /// </summary>
    public class MappingExecutionException : MiniMapperException
    {
        public Type? SourceType { get; }
        public Type? DestType { get; }
        public string? PropertyName { get; }

        public MappingExecutionException(string message, Type? sourceType = null, Type? destType = null, string? propertyName = null)
            : base(message)
        {
            SourceType = sourceType;
            DestType = destType;
            PropertyName = propertyName;
        }

        public MappingExecutionException(string message, Exception innerException, Type? sourceType = null, Type? destType = null, string? propertyName = null)
            : base(message, innerException)
        {
            SourceType = sourceType;
            DestType = destType;
            PropertyName = propertyName;
        }
    }
    /// <summary>
    /// 映射规则校验异常（启动时）
    /// </summary>
    public class MappingValidationException : MiniMapperException
    {
        public List<string> ValidationErrors { get; }

        public MappingValidationException(string message, List<string> validationErrors)
            : base(message)
        {
            ValidationErrors = validationErrors;
        }
    }
}
