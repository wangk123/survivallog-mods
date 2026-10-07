// 编译器 shim：BepInEx.Core.dll 内嵌的 NullableAttribute 缺少编译器所需构造函数（CS0656）。
// 在本项目内提供完整定义即可。
namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.All, AllowMultiple = false, Inherited = false)]
    internal sealed class NullableAttribute : Attribute
    {
        public NullableAttribute(byte b) { }
        public NullableAttribute(byte[] d) { }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Interface | AttributeTargets.Delegate, AllowMultiple = false, Inherited = false)]
    internal sealed class NullableContextAttribute : Attribute
    {
        public NullableContextAttribute(byte b) { }
    }

    [AttributeUsage(AttributeTargets.Module | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false, Inherited = false)]
    internal sealed class NullablePublicOnlyAttribute : Attribute
    {
        public NullablePublicOnlyAttribute(bool b) { }
    }
}
