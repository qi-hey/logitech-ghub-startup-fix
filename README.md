# Logitech G HUB 修复一直转圈圈无法进入启动项

适用于 Windows 10 / 11 上的 G HUB 启动故障。G HUB 一直停在 Logitech 标志，或者手动打开 `lghub_updater.exe` 后才能进入时，可以检查 Windows 的 `LGHUBUpdaterService`。

[下载 Windows 版](https://github.com/qi-hey/logitech-ghub-startup-fix/releases/latest)

## 使用方法

1. 在 Releases 下载 `Logitech-GHUB-Startup-Fix.exe`，双击运行。也可下载 ZIP，解压后运行 EXE。
2. 工具自动检查更新服务。显示「更新服务缺失」或「更新服务未运行」时，点击「修复服务」并确认 Windows 管理员授权。
3. 服务正常后，从开始菜单打开 Logitech G HUB。若界面提示重启 Windows，重启后再检查一次。

工具使用系统自带的 .NET Framework 4.8 或更高版本，适用于安装在系统 `Program Files\LGHUB` 目录的 G HUB。Release 中的 `SHA256SUMS.txt` 提供 EXE 和 ZIP 的 SHA-256 校验值。当前 EXE 为未签名构建，Windows 可能显示「未知发布者」。

## 修复原理

G HUB 安装程序会注册 `LGHUBUpdaterService`，启动命令为 `"C:\Program Files\LGHUB\lghub_updater.exe" --run-as-service`。当旧服务正在等待删除时，重新注册可能遇到 Windows 错误 `1072`，随后 G HUB 的界面会因更新服务缺失而一直加载。

修复前，工具通过 Windows 验证本机更新器的 Logitech 数字签名，并检查已有服务的命令、账户和类型。服务缺失时重建为开机自动启动，前两次故障各等待 5 秒后自动重启。已有服务停止或禁用时，恢复自动启动并启动服务。

- **错误 `1072`**　重启 Windows，等系统释放待删除的服务后重新运行工具。
- **检测到手动运行的更新器**　工具先恢复服务的自动启动设置，下次开机由 Windows 启动服务。
- **安装路径、签名或服务配置异常**　使用罗技官方安装程序修复 G HUB。

修复范围是 G HUB 更新服务的注册、启动方式和运行状态。

## 从源码构建

在项目根目录用 Windows 终端运行。

```bat
.\build.cmd
```

脚本使用本机的 .NET Framework C# 编译器，运行服务状态判断测试，并把 EXE、ZIP 和校验文件写入 `dist`。在 G HUB 服务正常运行的机器上，`.\build.cmd --verify-installed` 会额外验证实际的服务配置和数字签名。

- `src/Program.cs`　中文界面和 Windows 权限请求
- `src/HubService.cs`　检测、修复和错误提示
- `src/WindowsServices.cs`　Windows 服务管理接口
- `src/LogitechSignature.cs`　Logitech 更新器数字签名验证
