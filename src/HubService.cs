using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Threading;

namespace GHubStartupFix
{
    internal sealed class ServiceInfo
    {
        internal uint ServiceType, StartType, State;
        internal string BinaryPath, Account;
    }

    internal sealed class Diagnosis
    {
        internal readonly string Title, Detail;
        internal readonly bool CanRepair, Healthy;

        internal Diagnosis(string title, string detail, bool canRepair = false, bool healthy = false)
        {
            Title = title;
            Detail = detail;
            CanRepair = canRepair;
            Healthy = healthy;
        }
    }

    internal static class HubService
    {
        internal const string ServiceName = "LGHUBUpdaterService";
        internal static readonly string InstallRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "LGHUB");
        internal static readonly string UpdaterPath = Path.Combine(InstallRoot, "lghub_updater.exe");
        internal static readonly string ServiceCommand = "\"" + UpdaterPath + "\" --run-as-service";

        internal static Diagnosis Check()
        {
            // 检查只读取安装路径、服务配置与运行状态。
            if (!File.Exists(UpdaterPath))
                return new Diagnosis("未找到 G HUB 更新器",
                    "请检查 G HUB 是否安装在系统的 Program Files\\LGHUB 目录。");
            try
            {
                using (var manager = WindowsServices.OpenManager(false))
                using (var service = WindowsServices.Open(manager, false))
                    return Describe(service == null ? null : WindowsServices.Read(service));
            }
            catch (Win32Exception error)
            {
                return DescribeError(error);
            }
        }

        internal static Diagnosis Describe(ServiceInfo service)
        {
            if (service == null)
                return new Diagnosis("更新服务缺失",
                    "可重建 G HUB 更新服务并设为开机自动启动。", true);
            if (service.ServiceType != WindowsServices.OwnProcess ||
                !String.Equals(service.BinaryPath, ServiceCommand, StringComparison.OrdinalIgnoreCase) ||
                !(String.Equals(service.Account, "LocalSystem", StringComparison.OrdinalIgnoreCase) ||
                  String.Equals(service.Account, "NT AUTHORITY\\SYSTEM", StringComparison.OrdinalIgnoreCase)))
                return new Diagnosis("服务配置与默认安装不同",
                    "请用罗技官方安装程序修复 G HUB 的服务配置。");
            if (service.State == WindowsServices.Running && service.StartType == WindowsServices.Automatic)
                return new Diagnosis("更新服务正常",
                    "启动方式为自动，服务正在运行。可从开始菜单打开 G HUB。", false, true);
            if (service.State == WindowsServices.StartPending)
                return new Diagnosis("更新服务正在启动", "稍后重新检查服务状态。");
            if (service.State == WindowsServices.Running)
                return new Diagnosis("更新服务尚未设为自动启动",
                    "可将服务设为开机自动启动。", true);
            return new Diagnosis("更新服务未运行",
                "可恢复自动启动并尝试启动更新服务。", true);
        }

        internal static Diagnosis DescribeError(Win32Exception error)
        {
            if (error.NativeErrorCode == WindowsServices.ServiceMarkedForDelete)
                return new Diagnosis("服务正在等待删除（1072）",
                    "请重启 Windows，再打开本工具检查并修复。");
            return new Diagnosis("Windows 服务操作失败（" + error.NativeErrorCode + "）",
                error.Message);
        }

        internal static Diagnosis Repair()
        {
            Diagnosis current = Check();
            if (!current.CanRepair)
                return current;

            try
            {
                if (ProcessRunning("lghub_software_manager", false) || ProcessRunning("lghub_installer", false))
                    return new Diagnosis("G HUB 安装或更新正在运行", "安装结束后重新检查服务。");

                // 服务由 SYSTEM 加载。只接受默认安装位置下经 Windows 验证的罗技更新器。
                if (IsReparsePoint(InstallRoot) || IsReparsePoint(UpdaterPath))
                    return new Diagnosis("G HUB 安装路径使用了重定向",
                        "请用罗技官方安装程序修复默认位置的安装。");
                using (File.Open(UpdaterPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (!LogitechSignature.Verify(UpdaterPath))
                        return new Diagnosis("更新器的罗技签名验证失败",
                            "请用罗技官方安装程序修复更新器文件。");
                    return RepairVerifiedService();
                }
            }
            catch (Win32Exception error)
            {
                return DescribeError(error);
            }
            catch (IOException error)
            {
                return new Diagnosis("无法读取 G HUB 更新器", error.Message);
            }
            catch (UnauthorizedAccessException error)
            {
                return new Diagnosis("Windows 拒绝访问更新器", error.Message);
            }
            catch (CryptographicException error)
            {
                return new Diagnosis("Windows 无法读取数字签名", error.Message);
            }
        }

        private static Diagnosis RepairVerifiedService()
        {
            using (var manager = WindowsServices.OpenManager(true))
            {
                bool created;
                using (var service = OpenOrCreate(manager, out created))
                {
                    ServiceInfo info = WindowsServices.Read(service);
                    Diagnosis current = Describe(info);
                    if (!current.CanRepair)
                        return current;

                    int recoveryError = created ? WindowsServices.SetRecovery(service) : 0;
                    if (info.StartType != WindowsServices.Automatic)
                        WindowsServices.SetAutomatic(service);

                    // 手动打开的更新器可能占用 G HUB 的本地通信端口。
                    // 先恢复自启设置，留给下一次开机接管，避免打断正在使用的 G HUB。
                    if (info.State != WindowsServices.Running && ProcessRunning("lghub_updater", true))
                        return new Diagnosis("服务已设为自动启动",
                            "检测到手动运行的更新器。重启 Windows 后由服务接管。" + RecoveryNote(recoveryError));
                    if (info.State != WindowsServices.Running && info.State != WindowsServices.Stopped)
                        return new Diagnosis("服务已设为自动启动",
                            "Windows 服务处于过渡状态。重启 Windows 后重新检查。" + RecoveryNote(recoveryError));
                    if (info.State == WindowsServices.Stopped)
                        WindowsServices.StartUpdater(service);

                    var timeout = Stopwatch.StartNew();
                    WindowsServices.ServiceStatus status;
                    do
                    {
                        status = WindowsServices.ReadStatus(service);
                        if (status.State == WindowsServices.Running)
                            return new Diagnosis("更新服务已修复",
                                "服务正在运行并已设为开机自动启动。" + RecoveryNote(recoveryError), false, true);
                        if (status.State != WindowsServices.StartPending)
                            break;
                        Thread.Sleep(200);
                    } while (timeout.Elapsed < TimeSpan.FromSeconds(15));

                    return new Diagnosis("服务已设为自动启动",
                        "服务未进入运行状态（Windows 退出代码 " + status.ExitCode + "）。"
                        + "请重启 Windows 后重新检查。" + RecoveryNote(recoveryError));
                }
            }
        }

        private static ServiceHandle OpenOrCreate(ServiceHandle manager, out bool created)
        {
            created = false;
            ServiceHandle existing = WindowsServices.Open(manager, true);
            if (existing != null)
                return existing;
            try
            {
                ServiceHandle installed = WindowsServices.Create(manager);
                created = true;
                return installed;
            }
            catch (Win32Exception error)
            {
                if (error.NativeErrorCode != WindowsServices.ServiceAlreadyExists)
                    throw;
                // 并发安装刚恢复了服务时，重新读取配置再决定是否修复。
                existing = WindowsServices.Open(manager, true);
                if (existing == null)
                    throw;
                return existing;
            }
        }

        private static bool IsReparsePoint(string path)
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }

        private static bool ProcessRunning(string name, bool interactiveOnly)
        {
            Process[] instances = Process.GetProcessesByName(name);
            try
            {
                foreach (Process instance in instances)
                {
                    try
                    {
                        if (!instance.HasExited && (!interactiveOnly || instance.SessionId != 0))
                            return true;
                    }
                    catch (InvalidOperationException)
                    {
                        // 进程可能在枚举之后退出。
                    }
                }
                return false;
            }
            finally
            {
                foreach (Process instance in instances)
                    instance.Dispose();
            }
        }

        private static string RecoveryNote(int error)
        {
            return error == 0 ? String.Empty : " 故障重启策略设置失败（" + error + "）。";
        }
    }
}
