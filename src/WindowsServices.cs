using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace GHubStartupFix
{
    internal sealed class ServiceHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        private ServiceHandle() : base(true) { }

        protected override bool ReleaseHandle()
        {
            return WindowsServices.CloseServiceHandle(handle);
        }
    }

    internal static class WindowsServices
    {
        // 这些值对应 Windows 服务控制管理器返回的错误码和服务状态。
        internal const int ServiceMissing = 1060;
        internal const int ServiceAlreadyRunning = 1056;
        internal const int ServiceMarkedForDelete = 1072;
        internal const int ServiceAlreadyExists = 1073;
        internal const uint OwnProcess = 0x10;
        internal const uint Automatic = 2;
        internal const uint Stopped = 1;
        internal const uint StartPending = 2;
        internal const uint Running = 4;

        private const uint QueryConfig = 0x01;
        private const uint ChangeConfig = 0x02;
        private const uint QueryStatus = 0x04;
        private const uint Start = 0x10;
        private const uint ReadAccess = QueryConfig | QueryStatus;
        private const uint RepairAccess = ReadAccess | ChangeConfig | Start;
        private const uint NoChange = 0xffffffff;

        internal static ServiceHandle OpenManager(bool repair)
        {
            var handle = OpenSCManager(null, null, repair ? 0x03U : 0x01U);
            if (handle.IsInvalid)
                throw new Win32Exception();
            return handle;
        }

        internal static ServiceHandle Open(ServiceHandle manager, bool repair)
        {
            var handle = OpenService(manager, HubService.ServiceName, repair ? RepairAccess : ReadAccess);
            if (!handle.IsInvalid)
                return handle;

            int error = Marshal.GetLastWin32Error();
            handle.Dispose();
            if (error == ServiceMissing)
                return null;
            throw new Win32Exception(error);
        }

        internal static ServiceHandle Create(ServiceHandle manager)
        {
            // 使用罗技安装日志中的名称和命令。空账户对应 Windows 的 LocalSystem。
            var handle = CreateService(
                manager, HubService.ServiceName, "LGHUB Updater Service", RepairAccess,
                OwnProcess, Automatic, 1, HubService.ServiceCommand, null, IntPtr.Zero, null, null, null);
            if (handle.IsInvalid)
                throw new Win32Exception();
            return handle;
        }

        internal static ServiceInfo Read(ServiceHandle service)
        {
            // 系统先返回缓冲区大小，再将字符串和配置写入同一块内存。
            uint bytes;
            QueryServiceConfig(service, IntPtr.Zero, 0, out bytes);
            int error = Marshal.GetLastWin32Error();
            if (error != 122 || bytes == 0)
                throw new Win32Exception(error);

            IntPtr buffer = Marshal.AllocHGlobal(checked((int)bytes));
            try
            {
                if (!QueryServiceConfig(service, buffer, bytes, out bytes))
                    throw new Win32Exception();
                var config = (NativeConfig)Marshal.PtrToStructure(buffer, typeof(NativeConfig));
                return new ServiceInfo
                {
                    ServiceType = config.Type,
                    StartType = config.StartType,
                    BinaryPath = Marshal.PtrToStringUni(config.BinaryPath),
                    Account = Marshal.PtrToStringUni(config.Account),
                    State = ReadStatus(service).State
                };
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        internal static ServiceStatus ReadStatus(ServiceHandle service)
        {
            ServiceStatus status;
            if (!QueryServiceStatus(service, out status))
                throw new Win32Exception();
            return status;
        }

        internal static void SetAutomatic(ServiceHandle service)
        {
            if (!ChangeServiceConfig(service, NoChange, Automatic, NoChange, null, null,
                    IntPtr.Zero, null, null, null, null))
                throw new Win32Exception();
        }

        internal static int SetRecovery(ServiceHandle service)
        {
            // 前两次故障各等待五秒再重启。第三次停止重试，避免反复启动。
            int actionSize = Marshal.SizeOf(typeof(RecoveryAction));
            IntPtr actions = Marshal.AllocHGlobal(3 * actionSize);
            try
            {
                Marshal.StructureToPtr(new RecoveryAction { Type = 1, Delay = 5000 }, actions, false);
                Marshal.StructureToPtr(new RecoveryAction { Type = 1, Delay = 5000 }, IntPtr.Add(actions, actionSize), false);
                Marshal.StructureToPtr(new RecoveryAction(), IntPtr.Add(actions, 2 * actionSize), false);
                var config = new RecoveryConfig { ResetPeriod = 86400, Count = 3, Actions = actions };
                return ChangeServiceConfig2(service, 2, ref config) ? 0 : Marshal.GetLastWin32Error();
            }
            finally
            {
                Marshal.FreeHGlobal(actions);
            }
        }

        internal static void StartUpdater(ServiceHandle service)
        {
            if (StartService(service, 0, IntPtr.Zero))
                return;
            int error = Marshal.GetLastWin32Error();
            if (error != ServiceAlreadyRunning)
                throw new Win32Exception(error);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeConfig
        {
            internal uint Type, StartType, ErrorControl;
            internal IntPtr BinaryPath, LoadOrderGroup;
            internal uint TagId;
            internal IntPtr Dependencies, Account, DisplayName;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct ServiceStatus
        {
            internal uint Type, State, Controls, ExitCode, ServiceExitCode, CheckPoint, WaitHint;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RecoveryAction
        {
            internal uint Type, Delay;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RecoveryConfig
        {
            internal uint ResetPeriod;
            internal IntPtr RebootMessage, Command;
            internal uint Count;
            internal IntPtr Actions;
        }

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ServiceHandle OpenSCManager(string machine, string database, uint access);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ServiceHandle OpenService(ServiceHandle manager, string name, uint access);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern ServiceHandle CreateService(ServiceHandle manager, string name, string display,
            uint access, uint type, uint startType, uint errorControl, string path, string loadOrderGroup,
            IntPtr tagId, string dependencies, string user, string password);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool QueryServiceConfig(ServiceHandle service, IntPtr buffer, uint size, out uint needed);

        [DllImport("advapi32.dll", SetLastError = true)]
        private static extern bool QueryServiceStatus(ServiceHandle service, out ServiceStatus status);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ChangeServiceConfig(ServiceHandle service, uint type, uint startType, uint errorControl,
            string path, string loadOrderGroup, IntPtr tagId, string dependencies, string user, string password, string display);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool ChangeServiceConfig2(ServiceHandle service, uint level, ref RecoveryConfig config);

        [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool StartService(ServiceHandle service, uint argc, IntPtr argv);

        [DllImport("advapi32.dll")]
        internal static extern bool CloseServiceHandle(IntPtr handle);
    }
}
