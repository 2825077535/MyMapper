// See https://aka.ms/new-console-template for more information
using Microsoft.Extensions.DependencyInjection;
using MyMapper;

// 1. 初始化DI容器并配置双向映射
var services = new ServiceCollection();
var mappingConfig = new MappingConfiguration();

// 配置正向映射（User → UserDto）+ 反向映射（UserDto → User）
mappingConfig.CreateMap<User, UserDto>()
    .ForMember("Alias", "NickName")  // 自定义属性映射
    .IgnoreMember("Extra")           // 配置式忽略属性
    .ReverseMap()                    // 生成反向映射，自动复用规则
        .IgnoreMember("NickName");   // 反向映射自定义忽略（可选）

services.AddSingleton(mappingConfig);
services.AddSingleton<MiniMapper>();
var serviceProvider = services.BuildServiceProvider();
var mapper = serviceProvider.GetRequiredService<MiniMapper>();

// ===================== 测试1：正向映射（User → UserDto） =====================
Console.WriteLine("===== 正向映射（User → UserDto） =====");
var sourceUser = new User
{
    Id = 1001,
    UserName = "张三",
    Age = 25,
    Secret = "123456",    // 特性忽略
    Extra = "配置忽略内容",// 配置忽略
    NickName = "小张"     // 自定义映射
};
var userDto = mapper.Map<User, UserDto>(sourceUser);

// 验证正向映射结果
Console.WriteLine($"Id: {userDto.Id} (预期：1001) ");
Console.WriteLine($"UserName: {userDto.UserName} (预期：张三) ");
Console.WriteLine($"Age: {userDto.Age} (预期：25) ");
Console.WriteLine($"Secret: '{userDto.Secret}' (预期：空) ");
Console.WriteLine($"Extra: '{userDto.Extra}' (预期：空) ");
Console.WriteLine($"Alias: {userDto.Alias} (预期：小张) ");

// ===================== 测试2：反向映射（UserDto → User） =====================
Console.WriteLine("\n===== 反向映射（UserDto → User） =====");
// 修改UserDto数据，验证反向映射
userDto.Id = 1002;
userDto.UserName = "李四";
userDto.Age = 30;
userDto.Secret = "反向测试Secret"; // 反向无特性忽略，会映射
userDto.Extra = "反向测试Extra";   // 反向复用正向忽略规则，仍为空
userDto.Alias = "小李";            // 反向自定义映射（Alias → NickName）

var reverseUser = mapper.Map<UserDto, User>(userDto);

// 验证反向映射结果
Console.WriteLine($"Id: {reverseUser.Id} (预期：1002) ");
Console.WriteLine($"UserName: {reverseUser.UserName} (预期：李四) ");
Console.WriteLine($"Age: {reverseUser.Age} (预期：30) ");
Console.WriteLine($"Secret: {reverseUser.Secret} (预期：反向测试Secret) ");
Console.WriteLine($"Extra: '{reverseUser.Extra}' (预期：空) ");
Console.WriteLine($"NickName: {reverseUser.NickName} (预期：空，反向忽略) ");


// ===================== 测试代码 =====================
public class User
{
    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int Age { get; set; }

    // 反向映射时会被特性忽略（标记在UserDto的对应属性上）
    public string Secret { get; set; } = string.Empty;

    // 正向映射时配置式忽略，反向映射自动复用规则
    public string Extra { get; set; } = string.Empty;

    // 自定义规则映射（正向：User.NickName → UserDto.Alias）
    public string NickName { get; set; } = string.Empty;
}

// 目标实体：UserDto（包含特性忽略、自定义映射属性）
public class UserDto
{
    public UserDto() { }

    // 有参构造（参数名与源属性名匹配，默认映射）
    public UserDto(int id, string userName)
    {
        Id = id;
        UserName = userName;
    }

    public int Id { get; set; }
    public string UserName { get; set; } = string.Empty;
    public int Age { get; set; }

    // 正向映射时特性忽略该属性
    [MapperIgnore]
    public string Secret { get; set; } = string.Empty;

    public string Extra { get; set; } = string.Empty;

    // 自定义映射目标属性（正向：User.NickName → UserDto.Alias）
    public string Alias { get; set; } = string.Empty;
}