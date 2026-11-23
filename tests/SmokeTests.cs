using System;
using System.ComponentModel;
using System.IO;

namespace GHubStartupFix
{
    internal static class SmokeTests
    {
        private static int Main(string[] args)
        {
            try
            {
                var installed = new ServiceInfo
                {
                    ServiceType = WindowsServices.OwnProcess,
                    StartType = WindowsServices.Automatic,
                    State = WindowsServices.Running,
                    BinaryPath = HubService.ServiceCommand,
                    Account = "LocalSystem"
                };
                Assert(HubService.Describe(null).CanRepair, "缺失服务应允许修复");
                Assert(HubService.Describe(installed).Healthy, "自动且运行中的服务应显示正常");
                Assert(!HubService.Describe(installed).CanRepair, "正常服务无需改动");
                installed.StartType = 4;
                Assert(HubService.Describe(installed).CanRepair, "禁用的服务应允许恢复自启");
                installed.StartType = WindowsServices.Automatic;
                installed.State = WindowsServices.Stopped;
                Assert(HubService.Describe(installed).CanRepair, "停止的服务应允许启动");
                installed.State = WindowsServices.StartPending;
                Assert(!HubService.Describe(installed).CanRepair, "启动中的服务应等待检查");
                installed.State = WindowsServices.Stopped;
                installed.Account = "NT AUTHORITY\\LocalService";
                Assert(!HubService.Describe(installed).CanRepair, "账户异常时应阻止修复");
                installed.Account = "LocalSystem";
                installed.BinaryPath = HubService.UpdaterPath + " --run-as-service";
                Assert(!HubService.Describe(installed).CanRepair, "命令缺少路径引号时应阻止修复");
                var pending = HubService.DescribeError(new Win32Exception(WindowsServices.ServiceMarkedForDelete));
                Assert(!pending.CanRepair && pending.Detail.Contains("重启 Windows"), "1072 应提示重启");

                if (args.Length == 1 && args[0] == "--installed")
                {
                    // 本机集成检查只读取服务配置和数字签名。
                    Assert(HubService.Check().Healthy, "本机的 G HUB 服务应处于正常状态");
                    using (File.Open(HubService.UpdaterPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                        Assert(LogitechSignature.Verify(HubService.UpdaterPath), "应通过 Logitech 签名验证");
                    Assert(!LogitechSignature.Verify(typeof(SmokeTests).Assembly.Location),
                        "未签名的测试程序应被拒绝");
                    Console.WriteLine("Installed service and Authenticode checks passed.");
                }

                Console.WriteLine("Service decision tests passed.");
                return 0;
            }
            catch (Exception error)
            {
                Console.Error.WriteLine(error);
                return 1;
            }
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
                throw new InvalidOperationException(message);
        }
    }
}
