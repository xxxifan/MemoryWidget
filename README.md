# Memory Widget for Windows 11

![Platform](https://img.shields.io/badge/platform-Windows%2011-blue)
![.NET](https://img.shields.io/badge/.NET-8-purple)
![License](https://img.shields.io/badge/license-MIT-green)

一个 Windows 11 小组件，实时展示系统内存占用情况，支持一键清理工作集。

## 功能

- 展示内存占用率（百分比）
- 展示已用 / 总量 / 可用内存（GiB）
- 面板打开期间每 2 秒自动刷新，关闭后停止刷新节省资源
- 一键清理所有进程工作集，释放物理内存

## 环境要求

- Windows 11 22H2 或更高版本
- .NET SDK 8.x
- Windows 开发者模式（设置 → 系统 → 开发者选项）

## 构建

```powershell
.\scripts\build-dev-msix.ps1
```

产物输出到 `artifacts\`：

| 文件 | 说明 |
|------|------|
| `MemoryWidgetProvider.Dev.msix` | 安装包 |
| `cert\MemoryWidgetProvider.Dev.cer` | 自签名证书 |

## 安装（侧载）

在**管理员 PowerShell** 中执行：

```powershell
.\scripts\install-dev-msix.ps1
```

脚本会自动将证书导入 `LocalMachine\TrustedPeople` 和 `LocalMachine\Root`，然后安装 MSIX。

安装后按 `Win + W` 打开小组件面板，点击"添加小组件"找到"内存清理"并固定即可。

## 项目结构

```
MemoryWidgetProvider/
├── Program.cs               # 入口，COM 服务器注册
├── WidgetProvider.cs        # IWidgetProvider 实现，生命周期管理
├── SystemMemoryReader.cs    # GlobalMemoryStatusEx 封装
├── MemoryCleaner.cs         # 工作集清理（EmptyWorkingSet）
└── FactoryHelper.cs         # COM 工厂辅助

MemoryWidgetProvider.Package/
├── Package.appxmanifest     # MSIX 清单
├── AssetsSource/assets.html # 图标与预览图源文件
├── Images/                  # 应用包图标
└── ProviderAssets/          # 小组件图标与选择器预览图

scripts/
├── build-dev-msix.ps1       # 编译 + 打包 + 签名
├── export-assets.ps1        # 从 assets.html 导出图标与预览图
└── install-dev-msix.ps1     # 证书导入 + 侧载安装
```

## 技术栈

- `.NET 8` + `Microsoft.WindowsAppSDK 2.x`
- `IWidgetProvider` 接口 + Adaptive Card JSON 渲染
- P/Invoke：`GlobalMemoryStatusEx`、`EmptyWorkingSet`

## License

[MIT](LICENSE)
