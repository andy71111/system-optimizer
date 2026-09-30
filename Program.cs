using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Win32;

namespace MemoryCleanerGUI
{
    public static class Program
    {
        [STAThread]
        static void Main()
        {
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                    e.ExceptionObject == null ? "unknown" : e.ExceptionObject.ToString());
            };
            Application.ThreadException += (s, e) =>
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                    e.Exception == null ? "unknown" : e.Exception.ToString());
            };
            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new FrmMain());
            }
            catch (Exception ex)
            {
                System.IO.File.WriteAllText(
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log"),
                    ex.ToString());
            }
        }
    }

    // ==================== 圆角按钮 ====================
    public class RoundedButton : Button
    {
        public Color FillColor = Color.FromArgb(52, 152, 219);
        public Color HoverColor = Color.FromArgb(41, 128, 185);
        public int Radius = 12;
        private bool hovering = false;

        public RoundedButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            ForeColor = Color.White;
            Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            Cursor = Cursors.Hand;
            Size = new Size(180, 44);
        }

        protected override void OnMouseEnter(EventArgs e) { hovering = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hovering = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs pe)
        {
            pe.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
            using (SolidBrush br = new SolidBrush(hovering ? HoverColor : FillColor))
                pe.Graphics.FillPath(br, path);
            TextRenderer.DrawText(pe.Graphics, Text, Font, ClientRectangle, ForeColor,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
        }

        public static GraphicsPath RoundedPath(Rectangle r, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    // ==================== 圆角面板 ====================
    public class CardPanel : Panel
    {
        public int Radius = 14;
        public Color Fill = Color.White;

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using (GraphicsPath path = RoundedButton.RoundedPath(new Rectangle(0, 0, Width - 1, Height - 1), Radius))
            using (SolidBrush br = new SolidBrush(Fill))
                e.Graphics.FillPath(br, path);
        }
    }

    // ==================== 存储清理项 ====================
    public class CleanItem
    {
        public string Name;
        public string Path;
        public bool IsRecycleBin;
        public long Size;
    }

    // ==================== 主窗体 ====================
    public class FrmMain : Form
    {
        // API
        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool EmptyWorkingSet(IntPtr hProcess);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);
        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetPhysicallyInstalledSystemMemory(out long kb);
        [DllImport("shell32.dll")]
        private static extern int SHEmptyRecycleBin(IntPtr hwnd, string root, uint flags);

        [StructLayout(LayoutKind.Sequential)]
        private struct PERFORMANCE_INFORMATION
        {
            public uint cb;
            public ulong CommitTotal, CommitLimit, CommitPeak, PhysicalTotal, PhysicalAvailable;
            public ulong SystemCache, KernelTotal, KernelPaged, KernelNonpaged, PageSize;
            public uint HandleCount, ProcessCount, ThreadCount;
        }
        [DllImport("psapi.dll")]
        private static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION pi, uint cb);

        private const uint PROCESS_QUERY_INFORMATION = 0x0400;
        private const uint PROCESS_SET_QUOTA = 0x0100;
        private const uint SHERB_NOCONFIRMATION = 0x1;
        private const uint SHERB_NOPROGRESSUI = 0x2;

        private readonly List<CleanItem> cleanItems = new List<CleanItem>();
        private readonly List<DriveInfo> drives = new List<DriveInfo>();

        // 内存页控件
        private Label lblPercentBig, lblTotal, lblAvail, lblUsed;
        private RoundedButton btnClean;
        private TextBox txtLog;
        private System.Windows.Forms.Timer refreshTimer;

        // 存储页控件
        private CheckedListBox chkItems;
        private Label lblScanStatus;
        private RoundedButton btnScan, btnCleanDisk;
        private bool scanning = false;

        // 磁盘页控件
        private ComboBox cmbDrive;
        private ProgressBar barDisk;
        private Label lblDiskTotal, lblDiskFree, lblDiskUsed, lblDiskPct;
        private CheckedListBox chkDiskItems;
        private Label lblDiskStatus;
        private RoundedButton btnDiskScan, btnDiskClean, btnBigScan;
        private bool diskScanning = false;
        private string currentDriveRoot = "";
        private RoundedButton btnCleanAllDrives;
        private CheckBox chkAutoStart;
        private bool suppressEvent = false;

        // 页面切换
        private Panel pageMem, pageDisk, pageDrive;
        private RoundedButton tabMem, tabDisk, tabDrive;

        public FrmMain()
        {
            BuildUI();
            InitCleanItems();
            InitDrives();
            InitAutoStart();
            RefreshMemory();
        }

        private void BuildUI()
        {
            Text = "系统优化工具";
            Font = new Font("Microsoft YaHei UI", 9F);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(240, 244, 248);
            ClientSize = new Size(560, 660);

            var header = new Panel { Dock = DockStyle.Top, Height = 66 };
            header.Paint += (s, e) =>
            {
                using (LinearGradientBrush br = new LinearGradientBrush(header.ClientRectangle,
                    Color.FromArgb(41, 60, 90), Color.FromArgb(52, 152, 219), 45f))
                    e.Graphics.FillRectangle(br, header.ClientRectangle);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                TextRenderer.DrawText(e.Graphics, "系统优化工具", new Font("Microsoft YaHei UI", 14F, FontStyle.Bold),
                    new Point(24, 12), Color.White);
                TextRenderer.DrawText(e.Graphics, "内存清理 · 存储清理 · 磁盘清理 · 安全可靠", new Font("Microsoft YaHei UI", 8.5F),
                    new Point(25, 41), Color.FromArgb(210, 225, 245));
            };
            Controls.Add(header);

            // 三个分段 Tab
            tabMem = new RoundedButton { Text = "内存清理", Radius = 10, Size = new Size(116, 40), Location = new Point(24, 84), FillColor = Color.FromArgb(52,152,219), HoverColor = Color.FromArgb(52,152,219) };
            tabDisk = new RoundedButton { Text = "存储清理", Radius = 10, Size = new Size(116, 40), Location = new Point(150, 84), FillColor = Color.FromArgb(180,190,205), HoverColor = Color.FromArgb(160,175,195) };
            tabDrive = new RoundedButton { Text = "磁盘清理", Radius = 10, Size = new Size(116, 40), Location = new Point(276, 84), FillColor = Color.FromArgb(180,190,205), HoverColor = Color.FromArgb(160,175,195) };
            tabMem.Click += (s, e) => SwitchPage(0);
            tabDisk.Click += (s, e) => SwitchPage(1);
            tabDrive.Click += (s, e) => SwitchPage(2);
            Controls.Add(tabMem);
            Controls.Add(tabDisk);
            Controls.Add(tabDrive);

            // ============ 内存页 ============
            pageMem = new Panel { Location = new Point(20, 136), Size = new Size(520, 500), BackColor = Color.Transparent };
            var cardMemInfo = new CardPanel { Location = new Point(0, 0), Size = new Size(520, 170), Radius = 16 };
            lblPercentBig = new Label { Text = "0%", Font = new Font("Microsoft YaHei UI", 40F, FontStyle.Bold), ForeColor = Color.FromArgb(52,152,219), AutoSize = false, Size = new Size(160, 90), Location = new Point(26, 34), TextAlign = ContentAlignment.MiddleCenter };
            cardMemInfo.Controls.Add(lblPercentBig);
            var lblTag = new Label { Text = "内存使用率", Font = new Font("Microsoft YaHei UI", 9F), ForeColor = Color.Gray, AutoSize = true, Location = new Point(72, 118) };
            cardMemInfo.Controls.Add(lblTag);
            var lblStatsTitle = new Label { Text = "详细状态", Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = Color.FromArgb(44,62,80), AutoSize = true, Location = new Point(220, 20) };
            cardMemInfo.Controls.Add(lblStatsTitle);
            lblTotal = StatLabel(220, 48);
            lblAvail = StatLabel(220, 76);
            lblUsed = StatLabel(220, 104);
            cardMemInfo.Controls.Add(lblTotal);
            cardMemInfo.Controls.Add(lblAvail);
            cardMemInfo.Controls.Add(lblUsed);
            pageMem.Controls.Add(cardMemInfo);

            btnClean = new RoundedButton { Text = "一键清理内存", Size = new Size(240, 52), Radius = 16, Location = new Point(140, 188) };
            btnClean.Click += BtnClean_Click;
            pageMem.Controls.Add(btnClean);

            var logCard = new CardPanel { Location = new Point(0, 258), Size = new Size(520, 230), Radius = 16 };
            var lblLogTitle = new Label { Text = "清理日志", Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold), ForeColor = Color.FromArgb(44,62,80), AutoSize = true, Location = new Point(16, 12) };
            logCard.Controls.Add(lblLogTitle);
            txtLog = new TextBox { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None, BackColor = Color.FromArgb(248,250,252), Font = new Font("Consolas", 9F), Location = new Point(16, 36), Size = new Size(488, 182) };
            logCard.Controls.Add(txtLog);
            pageMem.Controls.Add(logCard);

            // ============ 存储页 ============
            pageDisk = new Panel { Location = new Point(20, 136), Size = new Size(520, 500), BackColor = Color.Transparent, Visible = false };
            btnScan = new RoundedButton { Text = "扫描可清理项", Radius = 12, Size = new Size(150, 44), Location = new Point(0, 0), FillColor = Color.FromArgb(52,152,219) };
            btnScan.Click += BtnScan_Click;
            pageDisk.Controls.Add(btnScan);
            btnCleanDisk = new RoundedButton { Text = "清理选中项", Radius = 12, Size = new Size(150, 44), Location = new Point(170, 0), FillColor = Color.FromArgb(230,80,80), HoverColor = Color.FromArgb(200,60,60) };
            btnCleanDisk.Click += BtnCleanDisk_Click;
            pageDisk.Controls.Add(btnCleanDisk);
            lblScanStatus = new Label { Text = "点击「扫描」查看可清理空间", ForeColor = Color.Gray, Font = new Font("Microsoft YaHei UI", 9F), AutoSize = true, Location = new Point(340, 14) };
            pageDisk.Controls.Add(lblScanStatus);

            var listCard = new CardPanel { Location = new Point(0, 60), Size = new Size(520, 420), Radius = 16 };
            chkItems = new CheckedListBox { BorderStyle = BorderStyle.None, BackColor = Color.White, Font = new Font("Microsoft YaHei UI", 9.5F), CheckOnClick = true, Location = new Point(12, 12), Size = new Size(496, 380), ItemHeight = 34 };
            listCard.Controls.Add(chkItems);
            pageDisk.Controls.Add(listCard);
            var lblTip = new Label { Text = "回收站默认不勾选。仅删除可再生的临时/缓存，不触碰个人文件。", ForeColor = Color.FromArgb(180,60,60), Font = new Font("Microsoft YaHei UI", 8.5F), AutoSize = true, Location = new Point(0, 486) };
            pageDisk.Controls.Add(lblTip);

            // ============ 磁盘页 ============
            pageDrive = new Panel { Location = new Point(20, 136), Size = new Size(520, 500), BackColor = Color.Transparent, Visible = false };

            var driveCard = new CardPanel { Location = new Point(0, 0), Size = new Size(520, 170), Radius = 16 };
            var lblDriveSel = new Label { Text = "选择磁盘：", Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold), ForeColor = Color.FromArgb(44,62,80), AutoSize = true, Location = new Point(16, 18) };
            driveCard.Controls.Add(lblDriveSel);
            cmbDrive = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Font = new Font("Microsoft YaHei UI", 10F), Location = new Point(110, 14), Size = new Size(200, 28) };
            cmbDrive.SelectedIndexChanged += (s, e) => OnDriveSelected();
            driveCard.Controls.Add(cmbDrive);
            barDisk = new ProgressBar { Location = new Point(16, 58), Size = new Size(320, 18), Style = ProgressBarStyle.Continuous };
            driveCard.Controls.Add(barDisk);
            lblDiskTotal = StatLabel(16, 88);
            lblDiskFree = StatLabel(16, 114);
            lblDiskUsed = StatLabel(200, 88);
            lblDiskPct = StatLabel(200, 114);
            driveCard.Controls.Add(lblDiskTotal);
            driveCard.Controls.Add(lblDiskFree);
            driveCard.Controls.Add(lblDiskUsed);
            driveCard.Controls.Add(lblDiskPct);
            pageDrive.Controls.Add(driveCard);

            btnDiskScan = new RoundedButton { Text = "扫描清理项", Radius = 12, Size = new Size(150, 44), Location = new Point(0, 184), FillColor = Color.FromArgb(52,152,219) };
            btnDiskScan.Click += BtnDiskScan_Click;
            pageDrive.Controls.Add(btnDiskScan);
            btnDiskClean = new RoundedButton { Text = "清理选中项", Radius = 12, Size = new Size(150, 44), Location = new Point(170, 184), FillColor = Color.FromArgb(230,80,80), HoverColor = Color.FromArgb(200,60,60) };
            btnDiskClean.Click += BtnDiskClean_Click;
            pageDrive.Controls.Add(btnDiskClean);
            btnBigScan = new RoundedButton { Text = "分析大文件", Radius = 12, Size = new Size(150, 44), Location = new Point(340, 184), FillColor = Color.FromArgb(120,120,140) };
            btnBigScan.Click += BtnBigScan_Click;
            pageDrive.Controls.Add(btnBigScan);
            lblDiskStatus = new Label { Text = "选择磁盘后点击扫描", ForeColor = Color.Gray, Font = new Font("Microsoft YaHei UI", 9F), AutoSize = true, Location = new Point(0, 240) };
            pageDrive.Controls.Add(lblDiskStatus);

            var diskListCard = new CardPanel { Location = new Point(0, 268), Size = new Size(520, 200), Radius = 16 };
            chkDiskItems = new CheckedListBox { BorderStyle = BorderStyle.None, BackColor = Color.White, Font = new Font("Microsoft YaHei UI", 9.5F), CheckOnClick = true, Location = new Point(12, 10), Size = new Size(496, 180), ItemHeight = 30 };
            diskListCard.Controls.Add(chkDiskItems);
            pageDrive.Controls.Add(diskListCard);

            btnCleanAllDrives = new RoundedButton { Text = "一键清理所有盘", Radius = 12, Size = new Size(200, 44), Location = new Point(0, 478), FillColor = Color.FromArgb(52,152,219) };
            btnCleanAllDrives.Click += BtnCleanAllDrives_Click;
            pageDrive.Controls.Add(btnCleanAllDrives);

            chkAutoStart = new CheckBox { Text = "开机自动启动", Font = new Font("Microsoft YaHei UI", 9.5F), AutoSize = true, Location = new Point(220, 492), ForeColor = Color.FromArgb(60,72,90) };
            chkAutoStart.CheckedChanged += (s, ee) => { if (!suppressEvent) SetAutoStart(chkAutoStart.Checked); };
            pageDrive.Controls.Add(chkAutoStart);


            Controls.Add(pageMem);
            Controls.Add(pageDisk);
            Controls.Add(pageDrive);

            refreshTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            refreshTimer.Tick += (s, e) => RefreshMemory();
            refreshTimer.Start();

            Log("工具已就绪。可清理内存，切换「存储清理」释放系统缓存，或在「磁盘清理」查看磁盘占用。");
        }

        private Label StatLabel(int x, int y)
        {
            return new Label { Text = "--", Font = new Font("Microsoft YaHei UI", 9.5F), ForeColor = Color.FromArgb(60,72,90), AutoSize = true, Location = new Point(x, y) };
        }

        private void SwitchPage(int idx)
        {
            pageMem.Visible = idx == 0;
            pageDisk.Visible = idx == 1;
            pageDrive.Visible = idx == 2;
            tabMem.FillColor = idx == 0 ? Color.FromArgb(52,152,219) : Color.FromArgb(180,190,205);
            tabDisk.FillColor = idx == 1 ? Color.FromArgb(52,152,219) : Color.FromArgb(180,190,205);
            tabDrive.FillColor = idx == 2 ? Color.FromArgb(52,152,219) : Color.FromArgb(180,190,205);
        }

        private void InitCleanItems()
        {
            cleanItems.Clear();
            cleanItems.Add(new CleanItem { Name = "用户临时文件", Path = Path.GetTempPath(), IsRecycleBin = false });
            cleanItems.Add(new CleanItem { Name = "系统临时文件", Path = @"C:\Windows\Temp", IsRecycleBin = false });
            cleanItems.Add(new CleanItem { Name = "浏览器 / Internet 临时文件", Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\INetCache"), IsRecycleBin = false });
            cleanItems.Add(new CleanItem { Name = "缩略图缓存", Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer"), IsRecycleBin = false });
            cleanItems.Add(new CleanItem { Name = "回收站", Path = "", IsRecycleBin = true });
            chkItems.Items.Clear();
            foreach (var it in cleanItems)
                chkItems.Items.Add(FormatItem(it), !it.IsRecycleBin);
        }

        private void InitDrives()
        {
            drives.Clear();
            cmbDrive.Items.Clear();
            foreach (DriveInfo d in DriveInfo.GetDrives())
            {
                if (d.DriveType == DriveType.Fixed && d.IsReady)
                {
                    drives.Add(d);
                    cmbDrive.Items.Add(d.Name);
                }
            }
            if (cmbDrive.Items.Count > 0) cmbDrive.SelectedIndex = 0;
        }

        private void OnDriveSelected()
        {
            int idx = cmbDrive.SelectedIndex;
            if (idx < 0) return;
            DriveInfo d = drives[idx];
            currentDriveRoot = d.RootDirectory.FullName;
            long total = d.TotalSize;
            long free = d.AvailableFreeSpace;
            long used = total - free;
            double pct = total > 0 ? Math.Round(used * 100.0 / total, 1) : 0;
            lblDiskTotal.Text = "总容量：  " + FormatSize(total);
            lblDiskFree.Text = "可用空间：" + FormatSize(free);
            lblDiskUsed.Text = "已用空间：" + FormatSize(used);
            lblDiskPct.Text = "使用率：  " + pct + "%";
            barDisk.Maximum = 100;
            barDisk.Value = (int)Math.Min(100, pct);
            barDisk.ForeColor = pct > 90 ? Color.FromArgb(220,60,60) : (pct > 75 ? Color.FromArgb(230,140,40) : Color.FromArgb(52,152,219));
            chkDiskItems.Items.Clear();
            lblDiskStatus.Text = "选择磁盘后点击「扫描清理项」";
        }

        private string FormatItem(CleanItem it)
        {
            string size = it.Size > 0 ? FormatSize(it.Size) : "未扫描";
            return it.Name + "        " + size;
        }

        private static string FormatSize(long b)
        {
            if (b < 0) b = 0;
            if (b >= 1024L * 1024 * 1024) return Math.Round(b / 1024.0 / 1024 / 1024, 2) + " GB";
            if (b >= 1024 * 1024) return Math.Round(b / 1024.0 / 1024, 1) + " MB";
            return Math.Round(b / 1024.0) + " KB";
        }

        // ==================== 内存 ====================
        private void RefreshMemory()
        {
            long total = GetTotalMemory();
            long avail = GetAvailableMemory();
            if (total <= 0) return;
            long used = total - avail;
            double totalGB = Math.Round(total / 1024.0 / 1024 / 1024, 2);
            double availGB = Math.Round(avail / 1024.0 / 1024 / 1024, 2);
            double usedGB = Math.Round(used / 1024.0 / 1024 / 1024, 2);
            double percent = Math.Round(used * 100.0 / total, 1);
            lblPercentBig.Text = percent + "%";
            lblPercentBig.ForeColor = percent > 85 ? Color.FromArgb(220,60,60) : (percent > 70 ? Color.FromArgb(230,140,40) : Color.FromArgb(52,152,219));
            lblTotal.Text = "总内存      " + totalGB + " GB";
            lblAvail.Text = "可用内存  " + availGB + " GB";
            lblUsed.Text = "已用内存  " + usedGB + " GB";
        }

        private async void BtnClean_Click(object sender, EventArgs e)
        {
            btnClean.Enabled = false;
            btnClean.Text = "清理中…";
            Log("[" + DateTime.Now.ToString("HH:mm:ss") + "] 开始清理闲置内存…");
            long before = GetAvailableMemory();
            int ok = 0, skip = 0;
            await Task.Run(delegate
            {
                foreach (Process p in Process.GetProcesses())
                {
                    IntPtr h = IntPtr.Zero;
                    try
                    {
                        h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_SET_QUOTA, false, p.Id);
                        if (h != IntPtr.Zero) { EmptyWorkingSet(h); ok++; }
                        else skip++;
                    }
                    catch { skip++; }
                    finally { if (h != IntPtr.Zero) CloseHandle(h); }
                }
            });
            System.Threading.Thread.Sleep(200);
            long freed = GetAvailableMemory() - before;
            if (freed < 0) freed = 0;
            RefreshMemory();
            Log("[" + DateTime.Now.ToString("HH:mm:ss") + "] 完成：处理 " + ok + " 个进程，跳过 " + skip + " 个受保护进程，释放闲置内存 " + FormatSize(freed) + "。");
            btnClean.Text = "一键清理内存";
            btnClean.Enabled = true;
        }

        // ==================== 存储 ====================
        private async void BtnScan_Click(object sender, EventArgs e)
        {
            if (scanning) return;
            scanning = true;
            btnScan.Enabled = false;
            lblScanStatus.Text = "扫描中…";
            await Task.Run(delegate
            {
                foreach (var it in cleanItems)
                {
                    if (it.IsRecycleBin) it.Size = GetRecycleBinSize();
                    else it.Size = DirSize(it.Path);
                }
            });
            for (int i = 0; i < cleanItems.Count; i++)
                chkItems.Items[i] = FormatItem(cleanItems[i]);
            scanning = false;
            btnScan.Enabled = true;
            long sum = 0;
            foreach (var it in cleanItems) sum += it.Size;
            lblScanStatus.Text = "可清理合计约 " + FormatSize(sum);
            Log("[" + DateTime.Now.ToString("HH:mm:ss") + "] 存储扫描完成，可清理约 " + FormatSize(sum) + "。");
        }

        private async void BtnCleanDisk_Click(object sender, EventArgs e)
        {
            if (scanning) return;
            bool recycleChecked = false;
            List<CleanItem> selected = new List<CleanItem>();
            for (int i = 0; i < chkItems.Items.Count; i++)
                if (chkItems.GetItemChecked(i)) selected.Add(cleanItems[i]);
            foreach (var it in selected)
                if (it.IsRecycleBin) recycleChecked = true;

            if (recycleChecked)
            {
                var r = MessageBox.Show("回收站清理后不可恢复，确定要清空回收站吗？", "确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }

            scanning = true;
            btnCleanDisk.Enabled = false;
            lblScanStatus.Text = "清理中…";
            long freed = 0;
            await Task.Run(delegate
            {
                foreach (var it in selected)
                {
                    if (it.IsRecycleBin) { SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI); freed += it.Size; }
                    else freed += DeleteDirContents(it.Path);
                }
            });
            foreach (var it in cleanItems) { it.Size = 0; }
            for (int i = 0; i < cleanItems.Count; i++)
                chkItems.Items[i] = FormatItem(cleanItems[i]);
            scanning = false;
            btnCleanDisk.Enabled = true;
            lblScanStatus.Text = "已清理约 " + FormatSize(freed);
            Log("[" + DateTime.Now.ToString("HH:mm:ss") + "] 存储清理完成，释放约 " + FormatSize(freed) + "。");
        }

        // ==================== 磁盘页 ====================
        private List<CleanItem> BuildDriveItems(string root)
        {
            List<CleanItem> list = new List<CleanItem>();
            bool isSystem = Path.GetPathRoot(Environment.SystemDirectory).Equals(root, StringComparison.OrdinalIgnoreCase);
            if (isSystem)
            {
                list.Add(new CleanItem { Name = "用户临时文件", Path = Path.GetTempPath(), IsRecycleBin = false });
                list.Add(new CleanItem { Name = "系统临时文件", Path = @"C:\Windows\Temp", IsRecycleBin = false });
                list.Add(new CleanItem { Name = "浏览器 / Internet 临时文件", Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\INetCache"), IsRecycleBin = false });
                list.Add(new CleanItem { Name = "缩略图缓存", Path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Windows\Explorer"), IsRecycleBin = false });
            }
            list.Add(new CleanItem { Name = "该盘回收站", Path = root, IsRecycleBin = true });
            return list;
        }

        private async void BtnDiskScan_Click(object sender, EventArgs e)
        {
            if (diskScanning) return;
            if (string.IsNullOrEmpty(currentDriveRoot)) { lblDiskStatus.Text = "请先选择磁盘"; return; }
            diskScanning = true;
            btnDiskScan.Enabled = false;
            lblDiskStatus.Text = "扫描中…";
            List<CleanItem> items = BuildDriveItems(currentDriveRoot);
            List<long> sizes = new List<long>();
            await Task.Run(delegate
            {
                foreach (var it in items)
                {
                    if (it.IsRecycleBin) sizes.Add(GetRecycleBinSize());
                    else sizes.Add(DirSize(it.Path));
                }
            });
            chkDiskItems.Items.Clear();
            long sum = 0;
            for (int i = 0; i < items.Count; i++)
            {
                items[i].Size = sizes[i];
                sum += sizes[i];
                chkDiskItems.Items.Add(FormatItem(items[i]), !items[i].IsRecycleBin);
            }
            diskScanning = false;
            btnDiskScan.Enabled = true;
            lblDiskStatus.Text = currentDriveRoot + " 可清理合计约 " + FormatSize(sum);
            Log("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + currentDriveRoot + " 扫描完成，可清理约 " + FormatSize(sum) + "。");
        }

        private async void BtnDiskClean_Click(object sender, EventArgs e)
        {
            if (diskScanning) return;
            bool recycleChecked = false;
            List<CleanItem> items = BuildDriveItems(currentDriveRoot);
            List<CleanItem> selected = new List<CleanItem>();
            for (int i = 0; i < chkDiskItems.Items.Count && i < items.Count; i++)
                if (chkDiskItems.GetItemChecked(i)) selected.Add(items[i]);
            foreach (var it in selected)
                if (it.IsRecycleBin) recycleChecked = true;

            if (recycleChecked)
            {
                var r = MessageBox.Show("回收站清理后不可恢复，确定要清空回收站吗？", "确认",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (r != DialogResult.Yes) return;
            }

            diskScanning = true;
            btnDiskClean.Enabled = false;
            lblDiskStatus.Text = "清理中…";
            long freed = 0;
            await Task.Run(delegate
            {
                foreach (var it in selected)
                {
                    if (it.IsRecycleBin) { SHEmptyRecycleBin(IntPtr.Zero, null, SHERB_NOCONFIRMATION | SHERB_NOPROGRESSUI); freed += it.Size; }
                    else freed += DeleteDirContents(it.Path);
                }
            });
            for (int i = 0; i < chkDiskItems.Items.Count && i < items.Count; i++)
            {
                items[i].Size = 0;
                chkDiskItems.Items[i] = FormatItem(items[i]);
            }
            diskScanning = false;
            btnDiskClean.Enabled = true;
            lblDiskStatus.Text = currentDriveRoot + " 已清理约 " + FormatSize(freed);
            Log("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + currentDriveRoot + " 清理完成，释放约 " + FormatSize(freed) + "。");
        }

        private async void BtnBigScan_Click(object sender, EventArgs e)
        {
            if (diskScanning) return;
            if (string.IsNullOrEmpty(currentDriveRoot)) { lblDiskStatus.Text = "请先选择磁盘"; return; }
            diskScanning = true;
            btnBigScan.Enabled = false;
            btnBigScan.Text = "分析中…";
            lblDiskStatus.Text = "正在扫描 " + currentDriveRoot + " 的大文件，可能需要一点时间…";
            List<string> big = new List<string>();
            await Task.Run(delegate
            {
                List<KeyValuePair<long, string>> top = new List<KeyValuePair<long, string>>();
                try
                {
                    foreach (string f in Directory.EnumerateFiles(currentDriveRoot, "*", SearchOption.AllDirectories))
                    {
                        try
                        {
                            FileInfo fi = new FileInfo(f);
                            if (fi.Length >= 100 * 1024 * 1024)
                            {
                                top.Add(new KeyValuePair<long, string>(fi.Length, f));
                            }
                        }
                        catch { }
                    }
                }
                catch { }
                top.Sort((a, b) => b.Key.CompareTo(a.Key));
                int n = Math.Min(30, top.Count);
                for (int i = 0; i < n; i++)
                    big.Add(FormatSize(top[i].Key) + "   " + top[i].Value);
            });
            diskScanning = false;
            btnBigScan.Enabled = true;
            btnBigScan.Text = "分析大文件";
            lblDiskStatus.Text = currentDriveRoot + " 大文件分析完成。";
            ShowBigFiles(big);
        }

        private void InitAutoStart()
        {
            suppressEvent = true;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    string val = key == null ? null : (string)key.GetValue("SystemOptimizer");
                    chkAutoStart.Checked = !string.IsNullOrEmpty(val);
                }
            }
            catch { }
            suppressEvent = false;
        }

        private void SetAutoStart(bool enable)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run"))
                {
                    if (enable) key.SetValue("SystemOptimizer", "\"" + Application.ExecutablePath + "\"");
                    else key.DeleteValue("SystemOptimizer", false);
                }
                Log("[" + DateTime.Now.ToString("HH:mm:ss") + "] 开机自启已" + (enable ? "开启" : "关闭") + "。");
            }
            catch { }
        }

        private async void BtnCleanAllDrives_Click(object sender, EventArgs e)
        {
            if (diskScanning) return;
            diskScanning = true;
            btnCleanAllDrives.Enabled = false;
            btnCleanAllDrives.Text = "清理中…";
            lblDiskStatus.Text = "正在清理所有磁盘的临时/缓存…";
            long freed = 0;
            await Task.Run(delegate
            {
                foreach (DriveInfo d in DriveInfo.GetDrives())
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                    string root = d.RootDirectory.FullName;
                    List<CleanItem> items = BuildDriveItems(root);
                    foreach (var it in items)
                    {
                        if (it.IsRecycleBin) continue; // 一键清理不碰回收站
                        freed += DeleteDirContents(it.Path);
                    }
                }
            });
            diskScanning = false;
            btnCleanAllDrives.Enabled = true;
            btnCleanAllDrives.Text = "一键清理所有盘";
            lblDiskStatus.Text = "一键清理完成，共释放约 " + FormatSize(freed) + "（不含回收站）。";
            Log("[" + DateTime.Now.ToString("HH:mm:ss") + "] 一键清理所有盘完成，释放约 " + FormatSize(freed) + "。");
        }
        private void ShowBigFiles(List<string> big)
        {
            var f = new Form
            {
                Text = "大文件分析 - " + currentDriveRoot,
                StartPosition = FormStartPosition.CenterParent,
                Size = new Size(620, 460),
                Font = new Font("Microsoft YaHei UI", 9F),
                BackColor = Color.FromArgb(240,244,248)
            };
            var lb = new ListBox { Dock = DockStyle.Fill, BorderStyle = BorderStyle.None, BackColor = Color.White, Font = new Font("Consolas", 9.5F) };
            if (big.Count == 0) lb.Items.Add("未发现超过 100 MB 的大文件（或受权限限制）。");
            else foreach (string s in big) lb.Items.Add(s);
            var lblNote = new Label { Dock = DockStyle.Top, Height = 34, Text = "  以下文件仅作报告，需你自行决定是否处理。", ForeColor = Color.FromArgb(180,60,60), Font = new Font("Microsoft YaHei UI", 9F), TextAlign = ContentAlignment.MiddleLeft };
            f.Controls.Add(lb);
            f.Controls.Add(lblNote);
            f.ShowDialog();
        }

        // ==================== 文件工具 ====================
        private static long DirSize(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            long total = 0;
            try
            {
                foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return total;
        }

        private static long DeleteDirContents(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return 0;
            long freed = 0;
            try
            {
                foreach (string f in Directory.EnumerateFiles(dir))
                {
                    try { freed += new FileInfo(f).Length; File.Delete(f); } catch { }
                }
                foreach (string d in Directory.EnumerateDirectories(dir))
                {
                    try { Directory.Delete(d, true); } catch { }
                }
            }
            catch { }
            return freed;
        }

        private static long GetRecycleBinSize()
        {
            long total = 0;
            try
            {
                string sid = System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
                string recyclePath = Path.Combine(@"C:\$Recycle.Bin", sid);
                if (Directory.Exists(recyclePath)) total = DirSize(recyclePath);
            }
            catch { }
            return total;
        }

        private long GetTotalMemory()
        {
            long kb;
            if (GetPhysicallyInstalledSystemMemory(out kb)) return kb * 1024;
            return 0;
        }

        private long GetAvailableMemory()
        {
            PERFORMANCE_INFORMATION pi = new PERFORMANCE_INFORMATION();
            pi.cb = (uint)Marshal.SizeOf(typeof(PERFORMANCE_INFORMATION));
            PERFORMANCE_INFORMATION o;
            if (GetPerformanceInfo(out o, pi.cb)) return (long)(o.PhysicalAvailable * o.PageSize);
            return 0;
        }

        private void Log(string msg)
        {
            if (txtLog.TextLength > 8000) txtLog.Clear();
            txtLog.AppendText(msg + Environment.NewLine);
        }
    }
}
