// Screenshoter — лёгкий фоновый трей-апп для Windows.
// Хоткеи: Ctrl+Shift+1 — область (свой оверлей), Ctrl+Shift+3 — экран под курсором.
// После снимка: PNG в выбранную папку, в буфер кладётся И путь (текст), И картинка.
// Язык интерфейса: русский по умолчанию, английский — второй (переключение в трее).
// Собирается встроенным csc.exe (.NET Framework) — без установок. C# 5 совместимо.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

[assembly: AssemblyTitle("Screenshoter")]
[assembly: AssemblyProduct("Screenshoter")]
[assembly: AssemblyDescription("Скриншот области/экрана: сохраняет PNG и кладёт путь к файлу в буфер обмена")]
[assembly: AssemblyCompany("Evgenii Shapovalov")]
[assembly: AssemblyCopyright("© 2026 Evgenii Shapovalov")]
[assembly: AssemblyVersion("1.0.1.0")]
[assembly: AssemblyFileVersion("1.0.1.0")]

namespace Screenshoter
{
    static class Program
    {
        [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
        [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int value);
        [DllImport("user32.dll")] static extern bool SetProcessDPIAware();

        [STAThread]
        static void Main()
        {
            EnableDpiAwareness();

            bool createdNew;
            Mutex mtx = new Mutex(true, "Local\\Screenshoter_SingleInstance_8F3A1C", out createdNew);
            if (!createdNew) return;

            try
            {
                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new TrayContext());
            }
            finally
            {
                try { mtx.ReleaseMutex(); } catch { }
                mtx.Dispose();
            }
        }

        // PerMonitorV2 -> System -> базовая DPI-осведомлённость. Вызывать ДО создания окон.
        static void EnableDpiAwareness()
        {
            try { if (SetProcessDpiAwarenessContext((IntPtr)(-4))) return; } catch { }
            try { if (SetProcessDpiAwareness(2) == 0) return; } catch { }
            try { SetProcessDPIAware(); } catch { }
        }
    }

    // ---------------------------------------------------------------------
    // Трей-иконка, меню, реакция на хоткеи, локализация.
    // ---------------------------------------------------------------------
    class TrayContext : ApplicationContext
    {
        const string RepoUrl = "https://github.com/e-u-shapovalov/screenshoter";

        readonly NotifyIcon tray;
        readonly HotkeyWindow win;
        string folder;
        bool busy;
        bool english;
        System.Windows.Forms.Timer regTimer;
        bool announcedReady;

        public TrayContext()
        {
            english = LoadLang();
            folder = LoadFolder();
            try { Directory.CreateDirectory(folder); } catch { }

            win = new HotkeyWindow();
            win.HotkeyPressed += OnHotkey;

            tray = new NotifyIcon();
            tray.Icon = MakeIcon();
            tray.Visible = true;
            tray.DoubleClick += delegate { SafeRun(CaptureArea); };
            BuildMenu();

            win.TryRegister();
            UpdateTooltip();
            if (!win.AllOk)
            {
                tray.BalloonTipTitle = T("Жду горячие клавиши", "Waiting for hotkeys");
                tray.BalloonTipText = T(
                    "Ctrl+Shift+1 / Ctrl+Shift+3 заняты (Яндекс.Диск?). Отключи у него «сочетания клавиш» — перехвачу сам.",
                    "Ctrl+Shift+1 / Ctrl+Shift+3 are taken (Yandex.Disk?). Disable its shortcuts and I'll grab them automatically.");
                tray.ShowBalloonTip(5000);
            }
            else announcedReady = true;

            regTimer = new System.Windows.Forms.Timer();
            regTimer.Interval = 1500;
            regTimer.Tick += delegate { EnsureHotkeys(); };
            regTimer.Start();
        }

        // ru по умолчанию, en — второй
        string T(string ru, string en) { return english ? en : ru; }

        void BuildMenu()
        {
            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add(T("Скриншот области  (Ctrl+Shift+1)", "Capture region  (Ctrl+Shift+1)"), null,
                delegate { SafeRun(CaptureArea); });
            menu.Items.Add(T("Скриншот экрана  (Ctrl+Shift+3)", "Capture screen  (Ctrl+Shift+3)"), null,
                delegate { SafeRun(CaptureFullScreen); });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(T("Открыть папку скриншотов", "Open screenshots folder"), null,
                delegate { try { Process_Start(folder); } catch { } });
            menu.Items.Add(T("Изменить папку скриншотов…", "Change screenshots folder…"), null,
                delegate { ChangeFolder(); });
            menu.Items.Add(new ToolStripSeparator());

            ToolStripMenuItem addAuto = new ToolStripMenuItem(T("Добавить в автозапуск", "Add to startup"), null,
                delegate { AddAutostart(); });
            ToolStripMenuItem delAuto = new ToolStripMenuItem(T("Убрать из автозапуска", "Remove from startup"), null,
                delegate { RemoveAutostart(); });
            menu.Items.Add(addAuto);
            menu.Items.Add(delAuto);

            ToolStripMenuItem lang = new ToolStripMenuItem(T("Language", "Язык"));
            ToolStripMenuItem ru = new ToolStripMenuItem("Русский", null, delegate { SetLang(false); });
            ToolStripMenuItem en = new ToolStripMenuItem("English", null, delegate { SetLang(true); });
            ru.Checked = !english;
            en.Checked = english;
            lang.DropDownItems.Add(ru);
            lang.DropDownItems.Add(en);
            menu.Items.Add(lang);

            menu.Opening += delegate
            {
                bool on = IsAutostart();
                addAuto.Enabled = !on;
                delAuto.Enabled = on;
            };

            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(T("О программе (GitHub)", "About (GitHub)"), null,
                delegate { OpenUrl(RepoUrl); });
            menu.Items.Add(T("Выход", "Exit"), null, delegate { ExitApp(); });
            menu.Items.Add(new ToolStripSeparator());
            ToolStripMenuItem footer = new ToolStripMenuItem("Screenshoter v" + Ver() + "  ·  © Evgenii Shapovalov 2026");
            footer.Enabled = false;
            menu.Items.Add(footer);

            tray.ContextMenuStrip = menu; // старое меню оставляем GC — диспоз из его же клика небезопасен
        }

        void SetLang(bool en)
        {
            if (english == en) return;
            english = en;
            SaveLang(en);
            BuildMenu();
            UpdateTooltip();
            Notify(T("Язык изменён", "Language changed"), english ? "English" : "Русский");
        }

        void OnHotkey(int id)
        {
            if (id == HotkeyWindow.ID_AREA) SafeRun(CaptureArea);
            else if (id == HotkeyWindow.ID_FULL) SafeRun(CaptureFullScreen);
        }

        void SafeRun(Action act)
        {
            if (busy) return;
            busy = true;
            try { act(); }
            catch (Exception ex)
            {
                tray.BalloonTipTitle = T("Ошибка", "Error");
                tray.BalloonTipText = ex.Message;
                tray.ShowBalloonTip(3000);
            }
            finally { busy = false; }
        }

        void CaptureArea()
        {
            Rectangle vs = SystemInformation.VirtualScreen;
            using (Bitmap full = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(full))
                    g.CopyFromScreen(vs.X, vs.Y, 0, 0, vs.Size, CopyPixelOperation.SourceCopy);

                using (SelectionForm sel = new SelectionForm(full, vs.Location))
                {
                    if (sel.ShowDialog() == DialogResult.OK)
                    {
                        Rectangle r = Rectangle.Intersect(
                            sel.Selection, new Rectangle(0, 0, full.Width, full.Height));
                        if (r.Width > 2 && r.Height > 2)
                            using (Bitmap crop = full.Clone(r, full.PixelFormat))
                                SaveAndClip(crop);
                    }
                }
            }
        }

        void CaptureFullScreen()
        {
            Rectangle b = Screen.FromPoint(Cursor.Position).Bounds; // монитор под курсором
            using (Bitmap bmp = new Bitmap(b.Width, b.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(b.X, b.Y, 0, 0, b.Size, CopyPixelOperation.SourceCopy);
                SaveAndClip(bmp);
            }
        }

        void SaveAndClip(Bitmap bmp)
        {
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string path = Path.Combine(folder, stamp + ".png");
            int n = 2;
            while (File.Exists(path)) { path = Path.Combine(folder, stamp + "-" + n + ".png"); n++; }

            using (Bitmap toSave = new Bitmap(bmp))
                toSave.Save(path, ImageFormat.Png);

            SetClipboard(path, bmp);

            tray.BalloonTipTitle = T("Скрин сохранён", "Screenshot saved");
            tray.BalloonTipText = T("Путь в буфере — Ctrl+V", "Path copied — Ctrl+V") + "\n" + Path.GetFileName(path);
            tray.ShowBalloonTip(1200);
        }

        // в буфер кладём ОБА формата: текст-путь (для CLI) и картинку (для чатов/редакторов)
        static void SetClipboard(string path, Bitmap bmp)
        {
            DataObject data = new DataObject();
            data.SetData(DataFormats.UnicodeText, path);
            data.SetData(DataFormats.Text, path);
            data.SetData(DataFormats.Bitmap, new Bitmap(bmp));
            for (int i = 0; i < 6; i++)
            {
                try { Clipboard.SetDataObject(data, true); return; }
                catch { Thread.Sleep(60); }
            }
        }

        static void Process_Start(string target)
        {
            System.Diagnostics.Process.Start("explorer.exe", "\"" + target + "\"");
        }

        static void OpenUrl(string url)
        {
            try { System.Diagnostics.Process.Start(url); } catch { }
        }

        static string Ver()
        {
            System.Version v = Assembly.GetExecutingAssembly().GetName().Version;
            return v.Major + "." + v.Minor + "." + v.Build;
        }

        void ChangeFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = T("Куда сохранять скриншоты", "Where to save screenshots");
                if (Directory.Exists(folder)) dlg.SelectedPath = folder;
                if (dlg.ShowDialog() == DialogResult.OK && dlg.SelectedPath.Length > 0)
                {
                    folder = dlg.SelectedPath;
                    try { Directory.CreateDirectory(folder); } catch { }
                    SaveFolderSetting(folder);
                    Notify(T("Папка изменена", "Folder changed"), folder);
                }
            }
        }

        void AddAutostart()
        {
            try { CreateStartupShortcut(); Notify(T("Добавлено в автозапуск", "Added to startup"), T("Старт при входе в систему", "Starts at sign-in")); }
            catch (Exception ex) { Notify(T("Не удалось добавить", "Couldn't add"), ex.Message); }
        }

        void RemoveAutostart()
        {
            try
            {
                string p = StartupLnk();
                if (File.Exists(p)) File.Delete(p);
                Notify(T("Убрано из автозапуска", "Removed from startup"), T("Готово", "Done"));
            }
            catch (Exception ex) { Notify(T("Не удалось убрать", "Couldn't remove"), ex.Message); }
        }

        void Notify(string title, string text)
        {
            tray.BalloonTipTitle = title;
            tray.BalloonTipText = string.IsNullOrEmpty(text) ? " " : text;
            tray.ShowBalloonTip(1500);
        }

        static string SettingsDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Screenshoter");
        }
        static string SettingsPath() { return Path.Combine(SettingsDir(), "folder.txt"); }
        static string LangPath() { return Path.Combine(SettingsDir(), "lang.txt"); }

        static string LoadFolder()
        {
            try
            {
                string sp = SettingsPath();
                if (File.Exists(sp))
                {
                    string p = File.ReadAllText(sp).Trim();
                    if (p.Length > 0) return p;
                }
            }
            catch { }
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Screenshots");
        }

        static void SaveFolderSetting(string p)
        {
            try { Directory.CreateDirectory(SettingsDir()); File.WriteAllText(SettingsPath(), p); }
            catch { }
        }

        static bool LoadLang()
        {
            try { string p = LangPath(); if (File.Exists(p)) return File.ReadAllText(p).Trim().ToLowerInvariant() == "en"; }
            catch { }
            return false; // по умолчанию русский
        }

        static void SaveLang(bool en)
        {
            try { Directory.CreateDirectory(SettingsDir()); File.WriteAllText(LangPath(), en ? "en" : "ru"); }
            catch { }
        }

        static string StartupLnk()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Screenshoter.lnk");
        }

        static bool IsAutostart() { return File.Exists(StartupLnk()); }

        static void CreateStartupShortcut()
        {
            string exe = Application.ExecutablePath;
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            object shell = Activator.CreateInstance(t);
            try
            {
                object sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
                    null, shell, new object[] { StartupLnk() });
                Type st = sc.GetType();
                st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { exe });
                st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc,
                    new object[] { Path.GetDirectoryName(exe) });
                st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc,
                    new object[] { exe + ",0" });
                st.InvokeMember("Description", BindingFlags.SetProperty, null, sc,
                    new object[] { "Screenshoter" });
                st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
            }
            finally
            {
                try { Marshal.ReleaseComObject(shell); } catch { }
            }
        }

        void EnsureHotkeys()
        {
            if (!win.TryRegister()) return;
            UpdateTooltip();
            if (win.AllOk && !announcedReady)
            {
                announcedReady = true;
                tray.BalloonTipTitle = T("Screenshoter готов", "Screenshoter ready");
                tray.BalloonTipText = T("Ctrl+Shift+1 — область, Ctrl+Shift+3 — экран.",
                                        "Ctrl+Shift+1 — region, Ctrl+Shift+3 — screen.");
                tray.ShowBalloonTip(1500);
            }
        }

        void UpdateTooltip()
        {
            tray.Text = win.AllOk
                ? T("Screenshoter\nCtrl+Shift+1 — область, Ctrl+Shift+3 — экран",
                    "Screenshoter\nCtrl+Shift+1 — region, Ctrl+Shift+3 — screen")
                : T("Screenshoter — клавиши заняты (отключи хоткеи Яндекса)",
                    "Screenshoter — hotkeys busy (free them in the other app)");
        }

        // простая иконка (синяя «камера») рисуется в рантайме — не нужен .ico
        [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr handle);
        static Icon MakeIcon()
        {
            using (Bitmap bmp = new Bitmap(32, 32, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.Clear(Color.Transparent);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(45, 140, 255)))
                        g.FillRectangle(b, 3, 8, 26, 19);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(30, 90, 180)))
                        g.FillRectangle(b, 11, 4, 10, 6);
                    using (SolidBrush b = new SolidBrush(Color.White))
                        g.FillEllipse(b, 11, 11, 10, 10);
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(45, 140, 255)))
                        g.FillEllipse(b, 14, 14, 4, 4);
                }
                IntPtr h = bmp.GetHicon();
                try { return (Icon)Icon.FromHandle(h).Clone(); }
                finally { DestroyIcon(h); }
            }
        }

        void ExitApp()
        {
            if (regTimer != null) regTimer.Dispose();
            tray.Visible = false;
            win.Dispose();
            tray.Dispose();
            ExitThread();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                try { if (regTimer != null) regTimer.Dispose(); } catch { }
                try { if (tray != null) tray.Dispose(); } catch { }
                try { if (win != null) win.Dispose(); } catch { }
            }
            base.Dispose(disposing);
        }
    }

    // ---------------------------------------------------------------------
    // Скрытое окно — приёмник глобальных хоткеев (WM_HOTKEY).
    // ---------------------------------------------------------------------
    class HotkeyWindow : NativeWindow, IDisposable
    {
        public const int ID_AREA = 1;
        public const int ID_FULL = 2;

        const int WM_HOTKEY = 0x0312;
        const uint MOD_CONTROL = 0x0002;
        const uint MOD_SHIFT = 0x0004;
        const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mod, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public event Action<int> HotkeyPressed;

        bool areaOk, fullOk;
        public bool AllOk { get { return areaOk && fullOk; } }

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        // Пытается занять ещё не занятые хоткеи. Возвращает true, если что-то изменилось.
        public bool TryRegister()
        {
            uint mod = MOD_CONTROL | MOD_SHIFT | MOD_NOREPEAT;
            bool changed = false;
            if (!areaOk && RegisterHotKey(Handle, ID_AREA, mod, (uint)Keys.D1)) { areaOk = true; changed = true; }
            if (!fullOk && RegisterHotKey(Handle, ID_FULL, mod, (uint)Keys.D3)) { fullOk = true; changed = true; }
            return changed;
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_HOTKEY)
            {
                Action<int> h = HotkeyPressed;
                if (h != null) h(m.WParam.ToInt32());
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            if (areaOk) { try { UnregisterHotKey(Handle, ID_AREA); } catch { } }
            if (fullOk) { try { UnregisterHotKey(Handle, ID_FULL); } catch { } }
            DestroyHandle();
        }
    }

    // ---------------------------------------------------------------------
    // Оверлей выделения: затемнённый замороженный кадр (печём один раз),
    // выбранный прямоугольник — яркий, с рамкой и подписью размера.
    // Перерисовываем только изменившуюся полосу — без лагов на больших экранах.
    // ---------------------------------------------------------------------
    class SelectionForm : Form
    {
        readonly Bitmap shot;
        Bitmap dimmed;
        Point start;
        Rectangle sel;
        bool dragging;

        public Rectangle Selection { get { return sel; } }

        public SelectionForm(Bitmap shot, Point origin)
        {
            this.shot = shot;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = new Rectangle(origin, shot.Size);
            AutoScaleMode = AutoScaleMode.None;
            TopMost = true;
            ShowInTaskbar = false;
            DoubleBuffered = true;
            KeyPreview = true;
            Cursor = Cursors.Cross;
            BackColor = Color.Black;
            SetStyle(ControlStyles.OptimizedDoubleBuffer
                   | ControlStyles.AllPaintingInWmPaint
                   | ControlStyles.UserPaint, true);

            // Затемнённый фон печём ОДИН раз — на каждый кадр остаётся быстрый blit
            // вместо альфа-наложения по всему экрану (это и тормозило выделение).
            dimmed = new Bitmap(shot.Width, shot.Height, PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(dimmed))
            {
                g.DrawImageUnscaled(shot, 0, 0);
                using (SolidBrush b = new SolidBrush(Color.FromArgb(120, 0, 0, 0)))
                    g.FillRectangle(b, 0, 0, shot.Width, shot.Height);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            Focus();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right) { DialogResult = DialogResult.Cancel; Close(); return; }
            if (e.Button == MouseButtons.Left)
            {
                dragging = true;
                start = e.Location;
                sel = new Rectangle(e.Location, Size.Empty);
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging) return;
            Rectangle prev = sel;
            sel = Norm(start, e.Location);
            // перерисовываем только изменившуюся полосу вокруг рамки (+ запас на бордюр и подпись)
            Rectangle dirty = Rectangle.Union(prev, sel);
            dirty.Inflate(60, 60);
            Invalidate(dirty);
            Update();
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (dragging && e.Button == MouseButtons.Left)
            {
                dragging = false;
                sel = Norm(start, e.Location);
                DialogResult = (sel.Width > 2 && sel.Height > 2)
                    ? DialogResult.OK : DialogResult.Cancel;
                Close();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; Close(); }
        }

        static Rectangle Norm(Point a, Point b)
        {
            int x = Math.Min(a.X, b.X), y = Math.Min(a.Y, b.Y);
            int w = Math.Abs(a.X - b.X), h = Math.Abs(a.Y - b.Y);
            return new Rectangle(x, y, w, h);
        }

        protected override void OnPaintBackground(PaintEventArgs e) { /* всё рисуем в OnPaint */ }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.DrawImageUnscaled(dimmed, 0, 0);

            if (sel.Width > 0 && sel.Height > 0)
            {
                g.DrawImage(shot, sel, sel, GraphicsUnit.Pixel); // яркая область
                using (Pen pen = new Pen(Color.FromArgb(45, 140, 255), 1))
                    g.DrawRectangle(pen, sel.X, sel.Y, sel.Width - 1, sel.Height - 1);

                string label = sel.Width + " × " + sel.Height;
                using (Font f = new Font("Segoe UI", 9f))
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(210, 20, 20, 20)))
                using (SolidBrush fg = new SolidBrush(Color.White))
                {
                    SizeF sz = g.MeasureString(label, f);
                    float lx = sel.X;
                    float ly = sel.Y - sz.Height - 4;
                    if (ly < 0) ly = sel.Y + 4;
                    g.FillRectangle(bg, lx, ly, sz.Width + 8, sz.Height + 2);
                    g.DrawString(label, f, fg, lx + 4, ly + 1);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && dimmed != null) { dimmed.Dispose(); dimmed = null; }
            base.Dispose(disposing);
        }
    }
}
