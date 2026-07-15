using System.Runtime.InteropServices;
using COM;
using MemoryWidgetProvider;
using Microsoft.Windows.Widgets;

[DllImport("kernel32.dll")]
static extern IntPtr GetConsoleWindow();

[DllImport("ole32.dll")]
static extern int CoRegisterClassObject(
    [MarshalAs(UnmanagedType.LPStruct)] Guid rclsid,
    [MarshalAs(UnmanagedType.IUnknown)] object pUnk,
    uint dwClsContext,
    uint flags,
    out uint lpdwRegister);

[DllImport("ole32.dll")]
static extern int CoRevokeClassObject(uint dwRegister);

const uint CLSCTX_LOCAL_SERVER = 0x4;
const uint REGCLS_MULTIPLEUSE = 0x1;

uint cookie;
var registerResult = CoRegisterClassObject(
    ProviderContract.ProviderClassId,
    new WidgetProviderFactory<WidgetProvider>(),
    CLSCTX_LOCAL_SERVER,
    REGCLS_MULTIPLEUSE,
    out cookie);

if (registerResult < 0)
{
    ProviderLogger.Info($"CoRegisterClassObject 失败，HRESULT=0x{registerResult:X8}");
    Marshal.ThrowExceptionForHR(registerResult);
}

ProviderLogger.Info("CoRegisterClassObject 成功，Provider 已注册。");

if (GetConsoleWindow() != IntPtr.Zero)
{
    Console.WriteLine("Memory Widget Provider 已启动。按 Enter 退出。");
    Console.ReadLine();
    ProviderLogger.Info("控制台模式退出，执行 CoRevokeClassObject。");
    CoRevokeClassObject(cookie);
    return;
}

using var emptyWidgetListEvent = WidgetProvider.GetEmptyWidgetListEvent();
ProviderLogger.Info("等待 widget 列表为空事件。");
emptyWidgetListEvent.WaitOne();
ProviderLogger.Info("收到空列表事件，执行 CoRevokeClassObject。");
CoRevokeClassObject(cookie);
