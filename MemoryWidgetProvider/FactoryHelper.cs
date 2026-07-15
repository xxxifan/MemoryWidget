using System.Runtime.InteropServices;
using MemoryWidgetProvider;
using Microsoft.Windows.Widgets.Providers;
using WinRT;

namespace COM;

internal static class Guids
{
    public const string IClassFactory = "00000001-0000-0000-C000-000000000046";
    public const string IUnknown = "00000000-0000-0000-C000-000000000046";
}

[ComImport]
[ComVisible(false)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
[Guid(Guids.IClassFactory)]
internal interface IClassFactory
{
    [PreserveSig]
    int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject);

    [PreserveSig]
    int LockServer(bool fLock);
}

internal sealed class WidgetProviderFactory<T> : IClassFactory where T : IWidgetProvider, new()
{
    public int CreateInstance(IntPtr pUnkOuter, ref Guid riid, out IntPtr ppvObject)
    {
        ppvObject = IntPtr.Zero;
        ProviderLogger.Info($"Factory.CreateInstance: riid={riid}");

        if (pUnkOuter != IntPtr.Zero)
        {
            ProviderLogger.Info("Factory.CreateInstance: CLASS_E_NOAGGREGATION");
            return CLASS_E_NOAGGREGATION;
        }

        try
        {
            if (riid == typeof(IWidgetProvider).GUID || riid == typeof(T).GUID || riid == Guid.Parse(Guids.IUnknown))
            {
                var provider = new T();
                ppvObject = MarshalInspectable<IWidgetProvider>.FromManaged(provider);
                ProviderLogger.Info($"Factory.CreateInstance: success, ptr=0x{ppvObject.ToInt64():X}");
            }
            else
            {
                ProviderLogger.Info($"Factory.CreateInstance: E_NOINTERFACE, riid={riid}");
                Marshal.ThrowExceptionForHR(E_NOINTERFACE);
            }

            return 0;
        }
        catch (Exception ex)
        {
            ProviderLogger.Error("Factory.CreateInstance 异常", ex);
            return Marshal.GetHRForException(ex);
        }
    }

    public int LockServer(bool fLock)
    {
        return 0;
    }

    private const int CLASS_E_NOAGGREGATION = unchecked((int)0x80040110);
    private const int E_NOINTERFACE = unchecked((int)0x80004002);
}
