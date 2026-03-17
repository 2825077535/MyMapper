// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.DependencyInjection;
using MyMapper;



try
{
    // 初始化DI容器
    var services = new ServiceCollection();

    // 配置MiniMapper（含高级特性）
    services.AddMiniMapper(config =>
    {
        // 基础映射 + 条件映射
        config.CreateMap<User, UserDto>()
            .ForMemberWhen<User, UserDto>(
                "Alias",
                "NickName",
                (source, dest, sourceValue) => source.Age > 18 // 条件：年龄>18才映射Alias
            )
            .IgnoreMember("Extra")
            .ReverseMap();

        // 订单映射
        config.CreateMap<Order, OrderDto>().ReverseMap();
    }, converters =>
    {
        // 注册自定义转换器
        converters.Register(new BuiltInConverters.DateTimeToStringConverter());
        converters.Register(new BuiltInConverters.IntToUserStatusConverter());
    });

    // 构建服务提供器
    var provider = services.BuildServiceProvider();
    var mapper = provider.GetRequiredService<IMapper>();
    var mappingConfig = provider.GetRequiredService<MappingConfiguration>();

    // 校验映射规则
    Console.WriteLine("=== 开始校验映射规则 ===");
    mappingConfig.Validate();

    // 输出诊断信息
    Console.WriteLine("\n=== 映射规则诊断报告 ===");
    Console.WriteLine(mappingConfig.GetDiagnostics());

    // 测试1：条件映射
    Console.WriteLine("\n=== 测试：条件映射 ===");
    var minorUser = new User { Id = 1002, Age = 17, NickName = "小李四" };
    var minorDto = mapper.Map<User, UserDto>(minorUser);
    Console.WriteLine($"未成年人Alias：'{minorDto.Alias}'（条件不满足，应为空）");

    var adultUser = new User { Id = 1003, Age = 20, NickName = "大李四" };
    var adultDto = mapper.Map<User, UserDto>(adultUser);
    Console.WriteLine($"成年人Alias：'{adultDto.Alias}'（条件满足，应为'大李四'）");

    // 测试2：自定义类型转换器
    Console.WriteLine("\n=== 测试：自定义类型转换器 ===");
    var userWithConverter = new User { CreateTime = new DateTime(2026, 3, 16), Status = 1 };
    var dtoWithConverter = mapper.Map<User, UserDto>(userWithConverter);
    Console.WriteLine($"日期转换：{dtoWithConverter.CreateTime}（应为2026-03-16）");
    Console.WriteLine($"枚举转换：{dtoWithConverter.Status}（应为Active）");

    // 测试3：集合映射
    Console.WriteLine("\n=== 测试：集合映射 ===");
    var userList = new List<User>
    {
        new User { Id = 1, UserName = "张三", Age = 25 },
        new User { Id = 2, UserName = "李四", Age = 30 },
        new User { Id = 3, UserName = "王五", Age = 22 }
    };

    // 基础集合映射
    var dtoList = mapper.Map<User, UserDto>(userList);
    Console.WriteLine($"集合映射数量：{dtoList.Count}（应为3）");

    // 增量集合映射
    var existingDtoList = new List<UserDto>();
    mapper.Map<User, UserDto>(userList, existingDtoList);
    Console.WriteLine($"增量集合映射数量：{existingDtoList.Count}（应为3）");

    //测试4：单对象增量映射
    Console.WriteLine("\n=== 测试：单对象增量映射 ===");
    var existingDto = new UserDto
    {
        Id = 999,
        UserName = "旧用户名",
        Age = 99,
        Extra = "旧Extra",
        Alias = "旧Alias",
        CreateTime = "2000-01-01",
        Status = UserStatus.Locked
    };
    var updateUser = new User
    {
        Id = 1005,
        UserName = "增量更新用户",
        Age = 28,
        NickName = "增量别名",
        Extra = "新Extra",
        Secret = "不应映射",
        CreateTime = new DateTime(2026, 3, 16),
        Status = 1
    };
    mapper.Map(updateUser, existingDto);
    Console.WriteLine($"增量映射后 Id：{existingDto.Id}（应为1005）");
    Console.WriteLine($"增量映射后 UserName：{existingDto.UserName}（应为增量更新用户）");
    Console.WriteLine($"增量映射后 Alias：'{existingDto.Alias}'（应为增量别名）");
    Console.WriteLine($"增量映射后 Extra：'{existingDto.Extra}'（应仍为旧Extra，因被 IgnoreMember 忽略）");
    Console.WriteLine($"增量映射后 Secret：'{existingDto.Secret}'（应为空，因 MapperIgnore 忽略）");

    //测试5：忽略规则验证
    Console.WriteLine("\n=== 测试：忽略规则验证 ===");
    var ignoreUser = new User
    {
        Id = 1006,
        UserName = "忽略测试",
        Age = 26,
        Extra = "不应映射到DTO",
        Secret = "敏感信息"
    };
    var ignoreDto = mapper.Map<User, UserDto>(ignoreUser);
    Console.WriteLine($"Extra：'{ignoreDto.Extra}'（应为空，因 IgnoreMember 忽略）");
    Console.WriteLine($"Secret：'{ignoreDto.Secret}'（应为空，因 MapperIgnore 忽略）");

    //测试6：订单映射
    Console.WriteLine("\n=== 测试：订单映射 ===");
    var order = new Order
    {
        Id = 2001,
        OrderNo = "ORD20260317"
    };
    var orderDto = mapper.Map<Order, OrderDto>(order);
    Console.WriteLine($"Order -> OrderDto：Id={orderDto.Id}，OrderNo={orderDto.OrderNo}（应保持一致）");

    var reverseOrder = mapper.Map<OrderDto, Order>(orderDto);
    Console.WriteLine($"OrderDto -> Order：Id={reverseOrder.Id}，OrderNo={reverseOrder.OrderNo}（应保持一致）");

    //测试7：反向映射
    Console.WriteLine("\n=== 测试：反向映射 ===");
    var sourceDto = new UserDto
    {
        Id = 3001,
        UserName = "反向用户",
        Age = 31,
        Alias = "反向别名",
        Extra = "DTO Extra",
        CreateTime = "2026-03-16",
        Status = UserStatus.Active
    };
    var reverseUser = mapper.Map<UserDto, User>(sourceDto);
    Console.WriteLine($"反向映射 Id：{reverseUser.Id}（应为3001）");
    Console.WriteLine($"反向映射 UserName：{reverseUser.UserName}（应为反向用户）");
    Console.WriteLine($"反向映射 NickName：'{reverseUser.NickName}'（应为反向别名）");
    Console.WriteLine($"反向映射 Extra：'{reverseUser.Extra}'（应为空，因 ReverseMap 后仍忽略）");
    Console.WriteLine($"反向映射 Secret：'{reverseUser.Secret}'（应为空）");

    //测试8：空集合映射
    Console.WriteLine("\n=== 测试：空集合映射 ===");
    var emptyUsers = new List<User>();
    var emptyDtos = mapper.Map<User, UserDto>(emptyUsers);
    Console.WriteLine($"空集合映射数量：{emptyDtos.Count}（应为0）");
}
catch (MappingValidationException ex)
{
    Console.WriteLine("\n映射规则校验失败：");
    foreach (var error in ex.ValidationErrors)
    {
        Console.WriteLine($"  - {error}");
    }
}
catch (MappingExecutionException ex)
{
    Console.WriteLine($"\n映射执行失败：{ex.Message}");
    Console.WriteLine($"  源类型：{ex.SourceType?.Name}, 目标类型：{ex.DestType?.Name}, 出错属性：{ex.PropertyName}");
    Console.WriteLine($"  详细错误：{ex.InnerException?.Message}");
}
catch (Exception ex)
{
    Console.WriteLine($"\n未知错误：{ex.Message}");
    Console.WriteLine(ex.StackTrace);
}

Console.WriteLine("\n按任意键退出...");
Console.ReadKey();

/// <summary>用户实体</summary>
public class User
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int Age { get; set; }
    [MapperIgnore]
    public string Secret { get; set; } = string.Empty;
    public string Extra { get; set; } = string.Empty;
    public string NickName { get; set; } = string.Empty;
    public DateTime CreateTime { get; set; } = new DateTime(2026, 3, 16);
    public int Status { get; set; } = 1;
}

/// <summary>订单实体</summary>
public class Order
{
    public int Id { get; set; }
    public string OrderNo { get; set; } = string.Empty;
}

/// <summary>用户DTO（含转换器/条件映射字段）</summary>
public class UserDto
{
    public UserDto() { }

    public UserDto(int id, string userName)
    {
        Id = id;
        UserName = userName;
    }

    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int Age { get; set; }
    [MapperIgnore]
    public string Secret { get; set; } = string.Empty;
    public string Extra { get; set; } = string.Empty;
    public string Alias { get; set; } = string.Empty;
    public string CreateTime { get; set; } = string.Empty;
    public UserStatus Status { get; set; }
}

/// <summary>订单DTO</summary>
public class OrderDto
{
    public int Id { get; set; }
    public string OrderNo { get; set; } = string.Empty;
}