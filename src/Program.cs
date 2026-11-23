using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Reflection;
using System.Security.Principal;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("Logitech G HUB 修复一直转圈圈无法进入启动项")]
[assembly: AssemblyProduct("G HUB 启动修复")]
[assembly: AssemblyDescription("检查和修复 Windows 上的 G HUB 更新服务")]
[assembly: AssemblyVersion("1.0.0.0")]
[assembly: AssemblyFileVersion("1.0.0.0")]

namespace GHubStartupFix
{
    internal static class Program
    {
        internal const string Title = "Logitech G HUB 修复一直转圈圈无法进入启动项";
        internal const string ElevatedArgument = "--repair-elevated";

        [STAThread]
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args.Length == 1 && args[0] == ElevatedArgument)
            {
                // 提权进程只执行固定的服务修复操作，参数不接受文件或命令。
                Diagnosis result = IsAdministrator() ? HubService.Repair() :
                    new Diagnosis("需要管理员权限", "请在工具窗口中点击「修复服务」。");
                MessageBox.Show(result.Detail, result.Title, MessageBoxButtons.OK,
                    result.Healthy ? MessageBoxIcon.Information : MessageBoxIcon.Warning);
                return;
            }
            Application.Run(new RepairWindow());
        }

        internal static bool IsAdministrator()
        {
            using (WindowsIdentity identity = WindowsIdentity.GetCurrent())
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
    }

    internal sealed class RepairWindow : Form
    {
        private readonly Label status = new Label();
        private readonly Label detail = new Label();
        private readonly Button check = new Button();
        private readonly Button repair = new Button();

        internal RepairWindow()
        {
            Text = Program.Title;
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular);
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(600, 270);
            MinimumSize = Size;
            MaximizeBox = false;
            BackColor = Color.FromArgb(248, 249, 251);
            ForeColor = Color.FromArgb(31, 39, 47);

            var grid = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24, 20, 24, 18), RowCount = 4, ColumnCount = 1 };
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 46));
            grid.Controls.Add(new Label
            {
                Text = "G HUB 启动修复", Dock = DockStyle.Fill,
                Font = new Font(Font.FontFamily, 17F, FontStyle.Bold)
            }, 0, 0);
            status.Dock = DockStyle.Fill;
            status.TextAlign = ContentAlignment.BottomLeft;
            status.Font = new Font(Font.FontFamily, 13F, FontStyle.Bold);
            grid.Controls.Add(status, 0, 1);
            detail.Dock = DockStyle.Fill;
            detail.Padding = new Padding(0, 10, 0, 0);
            grid.Controls.Add(detail, 0, 2);

            var actions = new FlowLayoutPanel
            {
                Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, WrapContents = false
            };
            check.Text = "重新检查";
            check.Size = new Size(116, 34);
            repair.Text = "修复服务";
            repair.Size = new Size(116, 34);
            repair.FlatStyle = FlatStyle.Flat;
            repair.FlatAppearance.BorderSize = 0;
            repair.BackColor = Color.FromArgb(0, 99, 105);
            repair.ForeColor = Color.White;
            repair.EnabledChanged += delegate
            {
                repair.BackColor = repair.Enabled ? Color.FromArgb(0, 99, 105) : Color.FromArgb(225, 230, 232);
            };
            actions.Controls.Add(repair);
            actions.Controls.Add(check);
            grid.Controls.Add(actions, 0, 3);
            Controls.Add(grid);

            Shown += async delegate { await RunOperation(false); };
            check.Click += async delegate { await RunOperation(false); };
            repair.Click += async delegate { await RunOperation(true); };
        }

        private async Task RunOperation(bool fixing)
        {
            check.Enabled = repair.Enabled = false;
            status.ForeColor = ForeColor;
            status.Text = fixing ? "正在修复服务..." : "正在检查服务...";
            detail.Text = HubService.ServiceName;
            UseWaitCursor = true;
            Diagnosis result;
            try
            {
                if (fixing && !Program.IsAdministrator())
                {
                    await Task.Run(() =>
                    {
                        using (Process child = Process.Start(new ProcessStartInfo(Application.ExecutablePath)
                        {
                            Arguments = Program.ElevatedArgument,
                            UseShellExecute = true,
                            Verb = "runas"
                        }))
                        {
                            if (child != null)
                                child.WaitForExit();
                        }
                    });
                    result = await Task.Run(new Func<Diagnosis>(HubService.Check));
                }
                else
                {
                    result = await Task.Run<Diagnosis>(() => fixing ? HubService.Repair() : HubService.Check());
                }
            }
            catch (Win32Exception error)
            {
                result = error.NativeErrorCode == 1223
                    ? new Diagnosis("已取消管理员授权", "可重新点击「修复服务」。", true)
                    : HubService.DescribeError(error);
            }
            finally
            {
                UseWaitCursor = false;
                check.Enabled = true;
            }
            status.ForeColor = result.Healthy ? Color.FromArgb(0, 103, 74) : ForeColor;
            status.Text = result.Title;
            detail.Text = result.Detail;
            repair.Enabled = result.CanRepair;
        }
    }
}
