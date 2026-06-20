// Screenshoter — лёгкий фоновый трей-апп для Windows.
// Хоткеи: Ctrl+Shift+1 — область (свой оверлей),
//         Ctrl+Shift+3 — область с задержкой: выделяешь зону → 5 сек на наведение мыши
//                        (поймать всплывающий тултип/бабл) → снимок сам щёлкается,
//         Ctrl+Shift+2 — убрать путь из буфера (оставить только картинку последнего снимка).
// После снимка: PNG в выбранную папку, в буфер кладётся И путь (текст), И картинка.
// Язык интерфейса: русский по умолчанию, английский — второй (переключение в трее).
// Собирается встроенным csc.exe (.NET Framework) — без установок. C# 5 совместимо.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
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
[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

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
        Bitmap lastShot;   // последний снимок (картинка) — для toggle по Ctrl+Shift+2
        string lastPath;   // путь последнего снимка — чтобы вернуть в буфер по toggle
        bool pathInClip;   // сейчас в буфере путь+картинка (true) или только картинка (false)
        ToastForm toast;   // цветное уведомление (зелёный/синий) — заменяет balloon для toggle
        FlashForm flash;   // вспышка по снятой области
        static readonly Color ClipOnColor  = Color.FromArgb(38, 150, 70);   // зелёный — путь в буфере
        static readonly Color ClipOffColor = Color.FromArgb(40, 110, 200);  // синий — путь убран
        static readonly Color BusyColor    = Color.FromArgb(150, 60, 60);   // буфер занят
        bool busy;
        const int CountdownSeconds = 5;            // дефолт в диалоге задержки (Ctrl+Shift+3)
        CountdownForm countdown;                   // бейдж обратного отсчёта (null — не идёт)
        System.Windows.Forms.Timer cdTimer;        // тикает раз в секунду
        Rectangle cdRect;                          // что снимать в экранных координатах
        int cdLeft;                                // сколько секунд осталось
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
            menu.Items.Add(T("Снимок с задержкой 5с  (Ctrl+Shift+3)", "Delayed capture 5s  (Ctrl+Shift+3)"), null,
                delegate { SafeRun(CaptureAreaDelayed); });
            menu.Items.Add(T("Путь в буфере: вкл/выкл  (Ctrl+Shift+2)", "Toggle path in clipboard  (Ctrl+Shift+2)"), null,
                delegate { SafeRun(ClipToggle); });
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
            else if (id == HotkeyWindow.ID_FULL) SafeRun(CaptureAreaDelayed);
            else if (id == HotkeyWindow.ID_IMG) SafeRun(ClipToggle);
        }

        void SafeRun(Action act)
        {
            if (busy) return;
            busy = true;
            try { act(); }
            catch (Exception ex)
            {
                tray.BalloonTipTitle = T("Ошибка", "Error");
                // пустой Message → ShowBalloonTip сам бросит ArgumentException (вторичный краш)
                tray.BalloonTipText = string.IsNullOrEmpty(ex.Message) ? ex.GetType().Name : ex.Message;
                tray.ShowBalloonTip(3000);
            }
            finally { busy = false; }
        }

        void CaptureArea()
        {
            // идёт отсчёт задержки (Ctrl+Shift+3) — не накрываем экран своим оверлеем,
            // иначе CountdownTick через секунду снимет его в кадр вместо нужного контента
            if (countdown != null) return;
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
                        {
                            using (Bitmap crop = full.Clone(r, full.PixelFormat))
                                SaveAndClip(crop);
                            // вспышка по снятой области (оверлей уже закрыт, в кадр не попадёт)
                            ShowFlash(new Rectangle(r.X + vs.X, r.Y + vs.Y, r.Width, r.Height));
                        }
                    }
                }
            }
        }

        // Ctrl+Shift+3: выделяешь область как обычно, но снимок не делается сразу —
        // запускается обратный отсчёт. За это время наводишь мышь на элемент, чтобы
        // всплыл тултип/бабл, и по нулю та же область снимается с ЖИВОГО экрана.
        void CaptureAreaDelayed()
        {
            if (countdown != null) return; // отсчёт уже идёт — не запускаем второй

            Rectangle vs = SystemInformation.VirtualScreen;
            Rectangle screenRect;
            using (Bitmap full = new Bitmap(vs.Width, vs.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(full))
                    g.CopyFromScreen(vs.X, vs.Y, 0, 0, vs.Size, CopyPixelOperation.SourceCopy);

                using (SelectionForm sel = new SelectionForm(full, vs.Location,
                    T("Выдели область → потом спрошу задержку → снимок сам",
                      "Select area → then I'll ask the delay → auto-capture")))
                {
                    if (sel.ShowDialog() != DialogResult.OK) return;
                    Rectangle r = Rectangle.Intersect(
                        sel.Selection, new Rectangle(0, 0, full.Width, full.Height));
                    if (r.Width <= 2 || r.Height <= 2) return;
                    // в экранные координаты (оверлей начинался с vs.Location)
                    screenRect = new Rectangle(r.X + vs.X, r.Y + vs.Y, r.Width, r.Height);
                }
            }

            // спрашиваем задержку модально (по умолчанию 5 сек); оверлей уже закрыт
            int seconds;
            using (DelayPromptForm dlg = new DelayPromptForm(
                T("Задержка снимка", "Capture delay"),
                T("Через сколько секунд сделать снимок?", "Capture after how many seconds?"),
                T("сек", "sec"), "OK", T("Отмена", "Cancel"),
                CountdownSeconds, 1, 60))
            {
                if (dlg.ShowDialog() != DialogResult.OK) return;
                seconds = dlg.Seconds;
            }

            StartCountdownCapture(screenRect, seconds);
        }

        void StartCountdownCapture(Rectangle screenRect, int seconds)
        {
            cdRect = screenRect;
            cdLeft = seconds;

            countdown = new CountdownForm();
            countdown.SetNumber(cdLeft);
            countdown.PlaceFor(screenRect);
            countdown.Show();

            cdTimer = new System.Windows.Forms.Timer();
            cdTimer.Interval = 1000;
            cdTimer.Tick += CountdownTick;
            cdTimer.Start();
        }

        void CountdownTick(object sender, EventArgs e)
        {
            cdLeft--;
            if (cdLeft > 0)
            {
                if (countdown != null) countdown.SetNumber(cdLeft);
                return;
            }

            // время вышло — гасим бейдж и снимаем. Этот путь идёт НЕ через SafeRun,
            // поэтому ловим ошибки сами: иначе исключение из Timer.Tick валит весь апп.
            StopCountdown();
            try { CaptureScreenRect(cdRect); ShowFlash(cdRect); }
            catch (Exception ex)
            {
                tray.BalloonTipTitle = T("Ошибка", "Error");
                tray.BalloonTipText = string.IsNullOrEmpty(ex.Message) ? ex.GetType().Name : ex.Message;
                tray.ShowBalloonTip(3000);
            }
        }

        void StopCountdown()
        {
            if (cdTimer != null) { cdTimer.Stop(); cdTimer.Dispose(); cdTimer = null; }
            if (countdown != null)
            {
                countdown.Hide();
                countdown.Dispose();
                countdown = null;
            }
            // НЕ зовём Application.DoEvents(): он прокачивает очередь сообщений и пускает
            // реентранс (хоткей/таймерный тик посреди захвата → не та область, дубли).
            // Бейдж и так вне снимаемой области (PlaceFor), а его окно уже уничтожено Dispose().
        }

        // снимок прямоугольника с живого экрана (а не с замороженного кадра выделения)
        void CaptureScreenRect(Rectangle r)
        {
            using (Bitmap bmp = new Bitmap(r.Width, r.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                    g.CopyFromScreen(r.X, r.Y, 0, 0, r.Size, CopyPixelOperation.SourceCopy);
                SaveAndClip(bmp);
            }
        }

        void SaveAndClip(Bitmap bmp)
        {
            string stamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string path = Path.Combine(folder, stamp + ".png");
            int n = 2;
            while (File.Exists(path)) { path = Path.Combine(folder, stamp + "-" + n + ".png"); n++; }

            // bmp всегда независимый (Clone или свежий Bitmap) — лишняя копия для Save не нужна
            bmp.Save(path, ImageFormat.Png);

            bool clip = SetClipboard(path, bmp);

            // запоминаем снимок для Ctrl+Shift+2. Новую копию делаем ДО dispose старой:
            // если new Bitmap бросит (OOM), не останемся с уже освобождённым lastShot.
            Bitmap next = new Bitmap(bmp);
            if (lastShot != null) { try { lastShot.Dispose(); } catch { } }
            lastShot = next;
            lastPath = path;
            pathInClip = clip; // свежий снимок: в буфере путь+картинка (если буфер не был занят)

            tray.BalloonTipTitle = T("Скрин сохранён", "Screenshot saved");
            tray.BalloonTipText = (clip
                ? T("Путь в буфере — Ctrl+V", "Path copied — Ctrl+V")
                : T("Буфер занят — путь не скопирован", "Clipboard busy — path not copied"))
                + "\n" + Path.GetFileName(path);
            tray.ShowBalloonTip(1200);
        }

        // Ctrl+Shift+2: НЕ делает новый снимок — переключает буфер между «путь+картинка»
        // и «только картинка» для последнего снимка. Жмёшь — путь убрали (синий toast),
        // жмёшь ещё — путь вернули (зелёный). Так до следующего снимка (он сбрасывает в путь+картинка).
        void ClipToggle()
        {
            if (lastShot == null || lastPath == null)
            {
                ShowToast(BusyColor, T("Нет снимка", "No screenshot"),
                          T("Сначала сделай скрин (Ctrl+Shift+1)", "Capture first (Ctrl+Shift+1)"));
                return;
            }

            if (pathInClip)
            {
                // путь есть → убираем, оставляем только картинку (синий)
                if (SetClipboardImageOnly(lastShot))
                {
                    pathInClip = false;
                    ShowToast(ClipOffColor, T("Путь убран из буфера", "Path removed"),
                              T("Только картинка — Ctrl+V вставит изображение", "Image only — Ctrl+V pastes the picture"));
                }
                else ShowToast(BusyColor, T("Буфер занят", "Clipboard busy"), T("Попробуй ещё раз", "Try again"));
            }
            else
            {
                // пути нет → возвращаем путь+картинку (зелёный)
                if (SetClipboard(lastPath, lastShot))
                {
                    pathInClip = true;
                    ShowToast(ClipOnColor, T("Путь вернулся в буфер", "Path restored"),
                              T("Путь + картинка — Ctrl+V", "Path + image — Ctrl+V"));
                }
                else ShowToast(BusyColor, T("Буфер занят", "Clipboard busy"), T("Попробуй ещё раз", "Try again"));
            }
        }

        // цветной toast у трея (системный balloon красить нельзя — рисуем своё окно)
        void ShowToast(Color back, string title, string text)
        {
            if (toast != null) { try { toast.Close(); toast.Dispose(); } catch { } toast = null; }
            toast = new ToastForm(back, title, text);
            toast.Show();
        }

        // короткая вспышка по снятой области — «сфотографировалось»
        void ShowFlash(Rectangle screenRect)
        {
            if (flash != null) { try { flash.Close(); flash.Dispose(); } catch { } flash = null; }
            flash = new FlashForm(screenRect);
            flash.Show();
        }

        // в буфер кладём ОБА формата: текст-путь (для CLI) и картинку (для чатов/редакторов)
        static bool SetClipboard(string path, Bitmap bmp)
        {
            DataObject data = new DataObject();
            data.SetData(DataFormats.UnicodeText, path);
            data.SetData(DataFormats.Text, path);
            data.SetData(DataFormats.Bitmap, new Bitmap(bmp));
            return PushClipboard(data);
        }

        // только картинка — без текста-пути
        static bool SetClipboardImageOnly(Bitmap bmp)
        {
            DataObject data = new DataObject();
            data.SetData(DataFormats.Bitmap, new Bitmap(bmp));
            return PushClipboard(data);
        }

        // true — данные легли в буфер; false — буфер был занят все попытки
        static bool PushClipboard(DataObject data)
        {
            for (int i = 0; i < 6; i++)
            {
                try { Clipboard.SetDataObject(data, true); return true; }
                catch { Thread.Sleep(60); }
            }
            return false;
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
            object sc = null;
            try
            {
                sc = t.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod,
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
                try { if (sc != null) Marshal.ReleaseComObject(sc); } catch { }
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
                tray.BalloonTipText = T("Ctrl+Shift+1 — область, Ctrl+Shift+3 — с задержкой 5с, Ctrl+Shift+2 — только картинка.",
                                        "Ctrl+Shift+1 — region, Ctrl+Shift+3 — delayed 5s, Ctrl+Shift+2 — image only.");
                tray.ShowBalloonTip(1500);
            }
        }

        void UpdateTooltip()
        {
            // ВНИМАНИЕ: NotifyIcon.Text не длиннее 63 символов, иначе
            // ArgumentOutOfRangeException прямо в конструкторе → апп падает на старте.
            string s = win.AllOk
                ? T("Screenshoter: 1 область · 2 картинка · 3 задержка",
                    "Screenshoter: 1 region · 2 image · 3 delay")
                : T("Screenshoter — клавиши заняты (отключи хоткеи Яндекса)",
                    "Screenshoter — hotkeys busy (free them in the other app)");
            if (s.Length > 63) s = s.Substring(0, 63);
            tray.Text = s;
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
            if (cdTimer != null) { try { cdTimer.Dispose(); } catch { } cdTimer = null; }
            if (countdown != null) { try { countdown.Dispose(); } catch { } countdown = null; }
            if (toast != null) { try { toast.Dispose(); } catch { } toast = null; }
            if (flash != null) { try { flash.Dispose(); } catch { } flash = null; }
            if (lastShot != null) { try { lastShot.Dispose(); } catch { } lastShot = null; }
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
                try { if (cdTimer != null) cdTimer.Dispose(); } catch { }
                try { if (countdown != null) countdown.Dispose(); } catch { }
                try { if (toast != null) toast.Dispose(); } catch { }
                try { if (flash != null) flash.Dispose(); } catch { }
                try { if (lastShot != null) lastShot.Dispose(); } catch { }
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
        public const int ID_IMG = 3;

        const int WM_HOTKEY = 0x0312;
        const uint MOD_CONTROL = 0x0002;
        const uint MOD_SHIFT = 0x0004;
        const uint MOD_NOREPEAT = 0x4000;

        [DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mod, uint vk);
        [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public event Action<int> HotkeyPressed;

        bool areaOk, fullOk, imgOk;
        // готовность объявляем по двум основным хоткеям; «только картинка» (D2) — best-effort
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
            if (!imgOk && RegisterHotKey(Handle, ID_IMG, mod, (uint)Keys.D2)) { imgOk = true; changed = true; }
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
            if (imgOk) { try { UnregisterHotKey(Handle, ID_IMG); } catch { } }
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
        readonly string hint; // подсказка вверху по центру (для режима с задержкой); null — нет
        Bitmap dimmed;
        Point start;
        Rectangle sel;
        bool dragging;

        public Rectangle Selection { get { return sel; } }

        public SelectionForm(Bitmap shot, Point origin) : this(shot, origin, null) { }

        public SelectionForm(Bitmap shot, Point origin, string hint)
        {
            this.shot = shot;
            this.hint = hint;

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
                Capture = true; // держим мышь, даже если фокус украдут/курсор уедет
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
                Capture = false;
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

            if (hint != null) DrawHint(g);

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

        // подсказка-плашка вверху по центру — видна, пока выделяешь
        void DrawHint(Graphics g)
        {
            using (Font f = new Font("Segoe UI", 11f))
            {
                SizeF sz = g.MeasureString(hint, f);
                float w = sz.Width + 20, h = sz.Height + 12;
                float x = (Width - w) / 2f;
                float y = 24;
                using (SolidBrush bg = new SolidBrush(Color.FromArgb(220, 20, 20, 20)))
                using (SolidBrush fg = new SolidBrush(Color.White))
                {
                    g.FillRectangle(bg, x, y, w, h);
                    using (Pen pen = new Pen(Color.FromArgb(45, 140, 255), 1))
                        g.DrawRectangle(pen, x, y, w, h);
                    g.DrawString(hint, f, fg, x + 10, y + 6);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && dimmed != null) { dimmed.Dispose(); dimmed = null; }
            base.Dispose(disposing);
        }
    }

    // ---------------------------------------------------------------------
    // Бейдж обратного отсчёта: круг с цифрой. Клико-прозрачный и не забирает
    // фокус — чтобы под ним работали наведение мыши и всплывающие тултипы,
    // которые прячутся при смене фокуса/клике/нажатии клавиши.
    // ---------------------------------------------------------------------
    class CountdownForm : Form
    {
        int number;

        const int WS_EX_TRANSPARENT = 0x00000020; // мышь проходит насквозь
        const int WS_EX_NOACTIVATE  = 0x08000000; // показ не активирует окно
        const int WS_EX_TOOLWINDOW  = 0x00000080; // нет в Alt+Tab / панели задач

        public CountdownForm()
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            Size = new Size(64, 64);
            Opacity = 0.88;
            using (GraphicsPath gp = new GraphicsPath())
            {
                gp.AddEllipse(0, 0, Width, Height);
                Region = new Region(gp);
            }
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        public void SetNumber(int n) { number = n; Invalidate(); }

        // ставим бейдж рядом с областью, но ВНЕ неё — чтобы не попал в кадр.
        // Пробуем по очереди: сверху, снизу, справа, слева; если совсем некуда — угол монитора.
        public void PlaceFor(Rectangle r)
        {
            Rectangle mon = Screen.FromRectangle(r).Bounds;
            int cx = r.Left + (r.Width - Width) / 2;  // центр области по X
            int cy = r.Top + (r.Height - Height) / 2; // центр области по Y
            int x, y;

            if (r.Top - Height - 10 >= mon.Top)            { x = cx; y = r.Top - Height - 10; }
            else if (r.Bottom + 10 + Height <= mon.Bottom) { x = cx; y = r.Bottom + 10; }
            else if (r.Right + 10 + Width <= mon.Right)    { x = r.Right + 10; y = cy; }
            else if (r.Left - Width - 10 >= mon.Left)      { x = r.Left - Width - 10; y = cy; }
            else                                           { x = mon.Right - Width - 4; y = mon.Top + 4; }

            if (x < mon.Left) x = mon.Left + 4;
            if (x + Width > mon.Right) x = mon.Right - Width - 4;
            if (y < mon.Top) y = mon.Top + 4;
            if (y + Height > mon.Bottom) y = mon.Bottom - Height - 4;
            Location = new Point(x, y);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(Color.FromArgb(20, 20, 20)))
                g.FillEllipse(b, 1, 1, Width - 3, Height - 3);
            using (Pen p = new Pen(Color.FromArgb(45, 140, 255), 3))
                g.DrawEllipse(p, 3, 3, Width - 7, Height - 7);

            string s = number.ToString();
            using (Font f = new Font("Segoe UI", 24f, FontStyle.Bold))
            using (SolidBrush fg = new SolidBrush(Color.White))
            {
                SizeF sz = g.MeasureString(s, f);
                g.DrawString(s, f, fg, (Width - sz.Width) / 2f, (Height - sz.Height) / 2f);
            }
        }
    }

    // ---------------------------------------------------------------------
    // Модальный вопрос «через сколько секунд снимать?» с числовым полем.
    // Показывается после выделения области в режиме Ctrl+Shift+3.
    // Enter — подтвердить, Esc — отмена. DPI-масштабируется.
    // ---------------------------------------------------------------------
    class DelayPromptForm : Form
    {
        readonly NumericUpDown input;

        public int Seconds { get { return (int)input.Value; } }

        public DelayPromptForm(string title, string prompt, string unit,
                               string ok, string cancel, int def, int min, int max)
        {
            AutoScaleDimensions = new SizeF(96f, 96f);
            AutoScaleMode = AutoScaleMode.Dpi;

            Text = title;
            Font = new Font("Segoe UI", 9.75f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            TopMost = true;
            ClientSize = new Size(308, 124);

            Label lbl = new Label();
            lbl.Text = prompt;
            lbl.SetBounds(16, 16, 276, 36);

            input = new NumericUpDown();
            input.Minimum = min;
            input.Maximum = max;
            input.Value = Math.Min(Math.Max(def, min), max);
            input.TextAlign = HorizontalAlignment.Center;
            input.SetBounds(16, 58, 72, 28);

            Label unitLbl = new Label();
            unitLbl.Text = unit;
            unitLbl.AutoSize = true;
            unitLbl.SetBounds(94, 62, 60, 20);

            Button okBtn = new Button();
            okBtn.Text = ok;
            okBtn.DialogResult = DialogResult.OK;
            okBtn.SetBounds(128, 88, 80, 26);

            Button cancelBtn = new Button();
            cancelBtn.Text = cancel;
            cancelBtn.DialogResult = DialogResult.Cancel;
            cancelBtn.SetBounds(212, 88, 80, 26);

            Controls.Add(lbl);
            Controls.Add(input);
            Controls.Add(unitLbl);
            Controls.Add(okBtn);
            Controls.Add(cancelBtn);

            AcceptButton = okBtn;     // Enter
            CancelButton = cancelBtn; // Esc
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            input.Focus();
            input.Select(0, input.Text.Length); // выделить — можно сразу печатать
        }
    }

    // ---------------------------------------------------------------------
    // Вспышка по снятой области: белая заливка + акцентная рамка, быстро гаснет.
    // Клико-прозрачная и не забирает фокус. Показывается ПОСЛЕ захвата, в кадр не попадает.
    // ---------------------------------------------------------------------
    class FlashForm : Form
    {
        System.Windows.Forms.Timer life;
        int elapsed;
        const int Step = 30, TotalMs = 280;
        const double Start = 0.6;

        const int WS_EX_TRANSPARENT = 0x00000020;
        const int WS_EX_NOACTIVATE  = 0x08000000;
        const int WS_EX_TOOLWINDOW  = 0x00000080;

        public FlashForm(Rectangle screenRect)
        {
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            Bounds = screenRect;
            ShowInTaskbar = false;
            TopMost = true;
            DoubleBuffered = true;
            BackColor = Color.White;
            Opacity = Start;
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            life = new System.Windows.Forms.Timer();
            life.Interval = Step;
            life.Tick += Fade;
            life.Start();
        }

        void Fade(object s, EventArgs e)
        {
            elapsed += Step;
            double f = 1.0 - (double)elapsed / TotalMs;
            if (f <= 0) { life.Stop(); Close(); return; }
            Opacity = Start * f;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            using (Pen p = new Pen(Color.FromArgb(45, 140, 255), 3))
                e.Graphics.DrawRectangle(p, 1, 1, Width - 3, Height - 3);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && life != null) { life.Dispose(); life = null; }
            base.Dispose(disposing);
        }
    }

    // ---------------------------------------------------------------------
    // Цветной toast у трея. Layered-окно с ПОПИКСЕЛЬНОЙ альфой (UpdateLayeredWindow):
    // карточка рисуется в ARGB-битмап со сглаживанием + мягкая тень → гладкие края
    // (а не «лесенка» от Region) и плавный фейд. Клико-прозрачный, фокус не забирает.
    // ---------------------------------------------------------------------
    class ToastForm : Form
    {
        readonly Color back;
        readonly string title, text;
        Bitmap cardBmp;                 // готовая отрисовка — кэш для фейда
        System.Windows.Forms.Timer life;
        int elapsed;
        const int Step = 40, HoldMs = 1500, FadeMs = 600;
        const byte Peak = 245;          // пиковая альфа (~0.96)
        const int Pad = 16, Radius = 14;                         // поле под тень, скругление
        const int InsetX = 18, InsetTop = 13, InsetBottom = 14, LineGap = 5;
        readonly Font titleFont = new Font("Segoe UI", 13f, FontStyle.Bold);
        readonly Font bodyFont  = new Font("Segoe UI Semibold", 11f); // жирнее и крупнее
        int cardW, cardH;               // размер карточки — по фактическим метрикам текста

        const int WS_EX_LAYERED     = 0x00080000;
        const int WS_EX_TRANSPARENT  = 0x00000020;
        const int WS_EX_NOACTIVATE  = 0x08000000;
        const int WS_EX_TOOLWINDOW  = 0x00000080;
        const int ULW_ALPHA = 0x02;
        const byte AC_SRC_OVER = 0x00, AC_SRC_ALPHA = 0x01;

        [StructLayout(LayoutKind.Sequential, Pack = 1)]
        struct BLENDFUNCTION { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref Point pptDst, ref Size psize,
            IntPtr hdcSrc, ref Point pptSrc, int crKey, ref BLENDFUNCTION pblend, int dwFlags);
        [DllImport("user32.dll")] static extern IntPtr GetDC(IntPtr hWnd);
        [DllImport("user32.dll")] static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr CreateCompatibleDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);
        [DllImport("gdi32.dll")] static extern bool DeleteDC(IntPtr hDC);
        [DllImport("gdi32.dll")] static extern bool DeleteObject(IntPtr hObject);

        public ToastForm(Color back, string title, string text)
        {
            this.back = back;
            this.title = title;
            this.text = text;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            TopMost = true;

            // размер карточки считаем по реальным метрикам текста (учитываем локаль)
            using (Bitmap tmp = new Bitmap(1, 1))
            using (Graphics g = Graphics.FromImage(tmp))
            {
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                SizeF ts = g.MeasureString(title, titleFont);
                SizeF bs = g.MeasureString(text, bodyFont);
                int innerW = (int)Math.Ceiling(Math.Max(ts.Width, bs.Width));
                if (innerW > 520) innerW = 520;
                cardW = innerW + InsetX * 2;
                cardH = InsetTop + (int)Math.Ceiling(ts.Height) + LineGap + (int)Math.Ceiling(bs.Height) + InsetBottom;
            }

            Size = new Size(cardW + Pad * 2, cardH + Pad * 2);
            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            // правый-нижний угол: видимая карточка в 12px от края, остальное — поле под тень
            Location = new Point(wa.Right - 12 - cardW - Pad, wa.Bottom - 12 - cardH - Pad);
        }

        protected override bool ShowWithoutActivation { get { return true; } }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            cardBmp = Render();
            SetBitmap(cardBmp, Peak);
            life = new System.Windows.Forms.Timer();
            life.Interval = Step;
            life.Tick += Fade;
            life.Start();
        }

        void Fade(object s, EventArgs e)
        {
            elapsed += Step;
            if (elapsed <= HoldMs) return;
            double f = 1.0 - (double)(elapsed - HoldMs) / FadeMs;
            if (f <= 0) { life.Stop(); Close(); return; }
            SetBitmap(cardBmp, (byte)(Peak * f)); // перебиваем тот же кадр с меньшей альфой
        }

        Bitmap Render()
        {
            Bitmap bmp = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.AntiAlias;
                Rectangle card = new Rectangle(Pad, Pad, cardW, cardH);

                // мягкая тень: вложенные полупрозрачные заливки, плотность растёт к карточке
                for (int i = Pad; i >= 1; i--)
                    using (GraphicsPath sp = Rounded(Rectangle.Inflate(card, i, i), Radius + i))
                    using (SolidBrush sb = new SolidBrush(Color.FromArgb(5, 0, 0, 0)))
                        g.FillPath(sb, sp);

                // карточка со сглаженными краями
                using (GraphicsPath cp = Rounded(card, Radius))
                using (SolidBrush cb = new SolidBrush(back))
                    g.FillPath(cb, cp);

                using (SolidBrush fg = new SolidBrush(Color.White))
                {
                    SizeF ts = g.MeasureString(title, titleFont);
                    g.DrawString(title, titleFont, fg, card.X + InsetX, card.Y + InsetTop);
                    g.DrawString(text, bodyFont, fg, card.X + InsetX, card.Y + InsetTop + ts.Height + LineGap);
                }
            }
            return bmp;
        }

        // отдаём ARGB-битмап системе как содержимое окна (попиксельная альфа)
        void SetBitmap(Bitmap bmp, byte alpha)
        {
            IntPtr screenDc = GetDC(IntPtr.Zero);
            IntPtr memDc = CreateCompatibleDC(screenDc);
            IntPtr hBmp = IntPtr.Zero, old = IntPtr.Zero;
            try
            {
                hBmp = bmp.GetHbitmap(Color.FromArgb(0));
                old = SelectObject(memDc, hBmp);

                Size size = new Size(bmp.Width, bmp.Height);
                Point src = new Point(0, 0);
                Point dst = new Point(Left, Top);
                BLENDFUNCTION blend = new BLENDFUNCTION();
                blend.BlendOp = AC_SRC_OVER;
                blend.BlendFlags = 0;
                blend.SourceConstantAlpha = alpha;
                blend.AlphaFormat = AC_SRC_ALPHA;

                UpdateLayeredWindow(Handle, screenDc, ref dst, ref size, memDc, ref src, 0, ref blend, ULW_ALPHA);
            }
            finally
            {
                ReleaseDC(IntPtr.Zero, screenDc);
                if (hBmp != IntPtr.Zero) { SelectObject(memDc, old); DeleteObject(hBmp); }
                DeleteDC(memDc);
            }
        }

        static GraphicsPath Rounded(Rectangle r, int rad)
        {
            int d = rad * 2;
            GraphicsPath p = new GraphicsPath();
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (life != null) { life.Dispose(); life = null; }
                if (cardBmp != null) { cardBmp.Dispose(); cardBmp = null; }
                if (titleFont != null) titleFont.Dispose();
                if (bodyFont != null) bodyFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
