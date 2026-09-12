using System.Collections;
using System.Reflection;

namespace AutoRebalancedSpire;

/// <summary>
/// 把求解器自己登记的某个类型的镜像摘掉，好让我们登记改写过的版本。
/// </summary>
/// <remarks>
/// `MethodMirrorRegistry.Register` 用的是 `Dictionary.Add`，同一个类型登记第二次直接抛，
/// 求解器也没有「允许第三方改写」的入口。RebalancedSpire 改写的 33 张牌里有 5 张
/// （ConsumingShadow、Glasswork、Refract、Shatter、Spinner）求解器已经写了 bespoke 镜像，
/// 不摘掉就登记不上。
///
/// 参数写成 <c>object</c> 是因为求解器有两种注册表：不带返回值的
/// <c>MethodMirrorRegistry&lt;TBase, TContext&gt;</c> 和带返回值的三泛型版本，两边的字段名和形状一样。
///
/// 全部走反射，不去引用注册表内部那个私有的 `LookupResult` 类型 —— 那是个私有嵌套 record struct，
/// 在这边连名字都写不出来。反射只用到非泛型的 <see cref="IDictionary" />，两个字典都实现它。
///
/// **只在 mod 初始化时调用。** 注册表的说明写得很明白：所有登记必须在第一次查询之前完成，
/// 查过的类型会进写时复制的快照。这里摘完顺手把两层缓存都清掉，是为了万一有人提前查过。
/// </remarks>
internal static class RegistryOverride
{
    private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    /// <summary>
    /// 自检用：确认注册表内部那三个字段还在、类型也还对得上。
    /// </summary>
    /// <remarks>
    /// 这几个是求解器的私有字段，上游改名或换类型我们是收不到通知的。把它变成一条加载时的
    /// 干净失败，好过登记到一半抛在半途 —— 那时一部分牌已经登记、放行补丁还没装，
    /// 状态最难说清。
    /// </remarks>
    public static string? Probe(object registry)
    {
        foreach (string name in (string[])["_registrations", "_lookupCache"])
        {
            FieldInfo field = registry.GetType().GetField(name, Instance)
                ?? throw new MissingFieldException(registry.GetType().FullName, name);
            if (field.GetValue(registry) is not IDictionary)
                return $"求解器注册表的 {name} 不再是字典（{field.FieldType.Name}）。";
        }

        FieldInfo snapshot = registry.GetType().GetField("_resolvedSnapshot", Instance)
            ?? throw new MissingFieldException(registry.GetType().FullName, "_resolvedSnapshot");
        if (Activator.CreateInstance(snapshot.FieldType) is null)
            return $"造不出空的 {snapshot.FieldType.Name} 来清求解器注册表的解析快照。";
        return null;
    }

    /// <summary>摘掉 <paramref name="modelType" /> 已有的登记。返回是否真的摘掉了一条。</summary>
    public static bool DropRegistration(object registry, Type modelType)
    {
        IDictionary registrations = Field<IDictionary>(registry, "_registrations");
        if (!registrations.Contains(modelType))
            return false;

        registrations.Remove(modelType);
        ClearCaches(registry);
        return true;
    }

    private static void ClearCaches(object registry)
    {
        Field<IDictionary>(registry, "_lookupCache").Clear();

        FieldInfo snapshot = FieldInfo(registry.GetType(), "_resolvedSnapshot");
        snapshot.SetValue(registry, Activator.CreateInstance(snapshot.FieldType));
    }

    private static T Field<T>(object target, string name)
        => (T)(FieldInfo(target.GetType(), name).GetValue(target)
            ?? throw new InvalidOperationException($"求解器注册表的 {name} 是空的。"));

    private static FieldInfo FieldInfo(Type type, string name)
        => type.GetField(name, Instance)
            ?? throw new MissingFieldException(type.FullName, name);
}
