# ProviderAssets 说明

本目录与 `Images` 目录下的 PNG 由 `scripts\export-assets.ps1` 从 `AssetsSource\assets.html` 导出（名称必须和 `Package.appxmanifest` 一致）：

- `Memory_Icon.png`：小组件选择器与卡片标题栏图标（128x128）。
- `Memory_Screenshot_Dark.png` / `Memory_Screenshot_Light.png`：小组件选择器深色/浅色预览图（600x608，即中尺寸 300x304 的 2 倍）。
- `Images\StoreLogo.png`、`Square44x44Logo.png`、`Square150x150Logo.png`、`Wide310x150Logo.png`：应用包基础图标。

修改样式：编辑 `AssetsSource\assets.html` 后运行 `.\scripts\export-assets.ps1` 重新导出。构建时缺失的文件会以占位图代替。
