using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Kanaei
{
    internal class TrayApp : ApplicationContext
    {
        private readonly Config cfg;
        private readonly Form marshal;          // フックから UI スレッドへ戻るための隠しウィンドウ
        private readonly KeyHook hook;
        private readonly NotifyIcon tray;
        private readonly Timer stateTimer;
        private readonly Timer cleanupTimer;    // 押されたままの修飾キーを掃除するための遅延実行

        private readonly Icon iconEn;
        private readonly Icon iconJa;
        private readonly Icon iconOff;

        private ToolStripMenuItem miEnabled;
        private ToolStripMenuItem miStartup;
        private ToolStripMenuItem miPromote;
        private ToolStripMenuItem miIndicator;
        private ToolStripMenuItem miLog;
        private Hud hud;
        private bool? lastState = null;

        public TrayApp(Config cfg)
        {
            this.cfg = cfg;

            Log.Init(cfg.Path);
            Log.Enabled = cfg.DebugLog;
            Log.Write("=== {0} 起動 / exe={1} / 設定={2} ===", AppInfo.Name, AppInfo.ExePath, cfg.Path);

            marshal = new Form();
            marshal.ShowInTaskbar = false;
            marshal.FormBorderStyle = FormBorderStyle.FixedToolWindow;
            marshal.Opacity = 0;
            marshal.CreateControl();
            IntPtr forceHandle = marshal.Handle; // ハンドルを確定させる
            GC.KeepAlive(forceHandle);

            int px = Math.Max(16, SystemInformation.SmallIconSize.Width);
            iconEn = TrayIcon(px, Mark.English);
            iconJa = TrayIcon(px, Mark.Japanese);
            iconOff = TrayIcon(px, Mark.Disabled);

            hook = new KeyHook(cfg, marshal);
            hook.Tapped += OnTapped;
            hook.Install();
            Log.Write("キーフックを設定した。割り当て: {0}", Summary());

            tray = new NotifyIcon();
            tray.Icon = iconEn;
            tray.Visible = true;
            tray.ContextMenuStrip = BuildMenu();
            // タスクトレイのアイコンを左クリックするだけでオン・オフできる。
            tray.MouseClick += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left) SetEnabled(!Enabled);
            };
            UpdateTooltip();

            stateTimer = new Timer();
            stateTimer.Interval = 800;
            stateTimer.Tick += delegate { RefreshState(); };
            if (cfg.ShowImeStateInTray) stateTimer.Start();

            // 起動直後やスリープ復帰直後は、修飾キーが押されたままになっていることがある。
            // 少し落ち着いてから掃除する。
            cleanupTimer = new Timer();
            cleanupTimer.Interval = 1200;
            cleanupTimer.Tick += delegate
            {
                cleanupTimer.Stop();
                int n = Input.ReleaseStuckModifiers();
                if (n > 0)
                {
                    hook.ResetPending();   // 実際のキーの状態と食い違うので判定を捨てる
                    Log.Write("修飾キーを {0} 個解放した", n);
                }
            };
            ScheduleModifierCleanup("起動");

            SystemEvents.PowerModeChanged += OnPowerModeChanged;
            SystemEvents.SessionSwitch += OnSessionSwitch;

            tray.ShowBalloonTip(4000, AppInfo.Name,
                Summary() + "\nアイコンをクリックするとオン・オフを切り替えられます。", ToolTipIcon.Info);
        }

        private bool Enabled
        {
            get { return !hook.Paused; }
        }

        private static Icon TrayIcon(int px, Mark mark)
        {
            using (Bitmap bmp = Glyphs.Render(px, mark))
                return Glyphs.ToIcon(bmp, delegate(IntPtr h) { N.DestroyIcon(h); });
        }

        private string Summary()
        {
            return KeyNames.Name(cfg.LeftKey) + " = 英数 / " + KeyNames.Name(cfg.RightKey) + " = 日本語";
        }

        // --- 押されたままの修飾キーの掃除 ---

        private void ScheduleModifierCleanup(string why)
        {
            Log.Write("修飾キーの掃除を予約: {0}", why);
            cleanupTimer.Stop();
            cleanupTimer.Start();
        }

        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            RunOnUi(delegate { ScheduleModifierCleanup("スリープ復帰"); });
        }

        private void OnSessionSwitch(object sender, SessionSwitchEventArgs e)
        {
            if (e.Reason != SessionSwitchReason.SessionUnlock &&
                e.Reason != SessionSwitchReason.ConsoleConnect) return;
            RunOnUi(delegate { ScheduleModifierCleanup("ロック解除"); });
        }

        /// <summary>SystemEvents は別スレッドから来ることがあるので UI スレッドへ渡す。</summary>
        private void RunOnUi(Action action)
        {
            try
            {
                if (marshal.IsHandleCreated && marshal.InvokeRequired) marshal.BeginInvoke(action);
                else action();
            }
            catch { }
        }

        // --- メニュー ---

        private void SetEnabled(bool enable)
        {
            hook.Paused = !enable;
            if (miEnabled != null) miEnabled.Checked = enable;
            lastState = null;
            ApplyIcon();
            UpdateTooltip();
            Log.Write("{0}にした", enable ? "オン" : "オフ");
        }

        /// <summary>切り替え時の画面表示のオン・オフ。設定ファイルにも書き戻して次回以降も残す。</summary>
        private void SetIndicator(bool show)
        {
            cfg.ShowIndicator = show;
            if (!show && hud != null) hud.Hide();
            SaveConfig();
        }

        private void SetLogging(bool enable)
        {
            cfg.DebugLog = enable;
            Log.Enabled = enable;
            if (enable) Log.Write("=== 記録を開始した / exe={0} / 割り当て={1} ===", AppInfo.ExePath, Summary());
            SaveConfig();
        }

        private void SaveConfig()
        {
            try { cfg.Save(); }
            catch (Exception ex)
            {
                MessageBox.Show("設定を保存できませんでした。\n" + ex.Message + "\n\n"
                    + "この場では切り替えましたが、次回の起動には引き継がれません。", AppInfo.Name);
            }
        }

        private void ApplyIcon()
        {
            if (!Enabled) { tray.Icon = iconOff; return; }
            tray.Icon = (lastState != null && lastState.Value) ? iconJa : iconEn;
        }

        private ContextMenuStrip BuildMenu()
        {
            ContextMenuStrip m = new ContextMenuStrip();
            m.Font = new Font("Yu Gothic UI", 9f);

            ToolStripMenuItem header = new ToolStripMenuItem(Summary());
            header.Enabled = false;
            m.Items.Add(header);
            m.Items.Add(new ToolStripSeparator());

            miEnabled = new ToolStripMenuItem("有効");
            miEnabled.CheckOnClick = true;
            miEnabled.Checked = Enabled;
            miEnabled.Click += delegate { SetEnabled(miEnabled.Checked); };
            m.Items.Add(miEnabled);

            ToolStripMenuItem miAssign = new ToolStripMenuItem("キー割り当てを変更...");
            miAssign.Click += delegate { ShowWizard(); };
            m.Items.Add(miAssign);

            ToolStripMenuItem miInspect = new ToolStripMenuItem("キーを調べる...");
            miInspect.Click += delegate { ShowInspector(); };
            m.Items.Add(miInspect);

            m.Items.Add(new ToolStripSeparator());

            miIndicator = new ToolStripMenuItem("切り替え時に「あ」「A」を表示");
            miIndicator.CheckOnClick = true;
            miIndicator.Checked = cfg.ShowIndicator;
            miIndicator.Click += delegate { SetIndicator(miIndicator.Checked); };
            m.Items.Add(miIndicator);

            miPromote = new ToolStripMenuItem("タスクバーに常に表示する");
            miPromote.CheckOnClick = true;
            miPromote.Checked = IsPromoted();
            miPromote.Click += delegate { SetPromoted(miPromote.Checked); };
            m.Items.Add(miPromote);

            miStartup = new ToolStripMenuItem("Windows 起動時に実行");
            miStartup.CheckOnClick = true;
            miStartup.Checked = IsStartupEnabled();
            miStartup.Click += delegate { SetStartup(miStartup.Checked); };
            m.Items.Add(miStartup);

            m.Items.Add(new ToolStripSeparator());
            m.Items.Add(BuildAdvancedMenu());

            ToolStripMenuItem miExit = new ToolStripMenuItem("終了");
            miExit.Click += delegate { ExitThread(); };
            m.Items.Add(miExit);

            return m;
        }

        private ToolStripMenuItem BuildAdvancedMenu()
        {
            ToolStripMenuItem advanced = new ToolStripMenuItem("詳細");

            ToolStripMenuItem miOpen = new ToolStripMenuItem("設定ファイルを開く");
            miOpen.Click += delegate { OpenInNotepad(cfg.Path); };
            advanced.DropDownItems.Add(miOpen);

            ToolStripMenuItem miReload = new ToolStripMenuItem("設定を再読み込み");
            miReload.Click += delegate { Reload(); };
            advanced.DropDownItems.Add(miReload);

            advanced.DropDownItems.Add(new ToolStripSeparator());

            miLog = new ToolStripMenuItem("動作を記録する");
            miLog.CheckOnClick = true;
            miLog.Checked = cfg.DebugLog;
            miLog.ToolTipText = "切り替わらないアプリがあるとき、何が起きているかを記録します。";
            miLog.Click += delegate { SetLogging(miLog.Checked); };
            advanced.DropDownItems.Add(miLog);

            ToolStripMenuItem miOpenLog = new ToolStripMenuItem("記録を開く");
            miOpenLog.Click += delegate
            {
                if (Log.FilePath == null || !File.Exists(Log.FilePath))
                {
                    MessageBox.Show("まだ記録がありません。\n「動作を記録する」を入れてから、"
                        + "うまくいかないアプリで切り替えを試してください。", AppInfo.Name);
                    return;
                }
                OpenInNotepad(Log.FilePath);
            };
            advanced.DropDownItems.Add(miOpenLog);

            advanced.DropDownItems.Add(new ToolStripSeparator());

            ToolStripMenuItem miUninstall = new ToolStripMenuItem("登録を消して終了...");
            miUninstall.Click += delegate { Uninstall(); };
            advanced.DropDownItems.Add(miUninstall);

            return advanced;
        }

        private static void OpenInNotepad(string path)
        {
            try { System.Diagnostics.Process.Start("notepad.exe", "\"" + path + "\""); }
            catch (Exception ex) { MessageBox.Show(ex.Message, AppInfo.Name); }
        }

        // --- 本来の仕事 ---

        private void OnTapped(bool toJapanese)
        {
            Ime.Set(toJapanese, cfg);
            lastState = toJapanese;
            ApplyIcon();
            UpdateTooltip();

            if (cfg.ShowIndicator)
            {
                if (hud == null) hud = new Hud();
                hud.Flash(toJapanese ? Mark.Japanese : Mark.English, cfg);
            }
        }

        private void RefreshState()
        {
            if (!Enabled) return;
            bool? s = Ime.Get();
            if (s == null || s == lastState) return;
            lastState = s;
            ApplyIcon();
            UpdateTooltip();
        }

        private void UpdateTooltip()
        {
            string state = !Enabled ? "オフ"
                : (lastState == null ? "待機中" : (lastState.Value ? "日本語" : "英数"));
            string text = AppInfo.Name + " - " + state + "\n" + Summary();
            if (text.Length > 63) text = text.Substring(0, 63);
            tray.Text = text;
        }

        private void ShowWizard()
        {
            using (WizardForm w = new WizardForm(hook, cfg))
            {
                if (w.ShowDialog() == DialogResult.OK)
                {
                    tray.ContextMenuStrip = BuildMenu();
                    UpdateTooltip();
                    Log.Write("割り当てを変更: {0}", Summary());
                    tray.ShowBalloonTip(3000, AppInfo.Name, Summary() + " に変更しました。", ToolTipIcon.Info);
                }
            }
        }

        private void ShowInspector()
        {
            InspectorForm f = new InspectorForm(hook);
            f.Show();
            f.Activate();
        }

        private void Reload()
        {
            Config fresh = Config.Load(cfg.Path);
            cfg.LeftKey = fresh.LeftKey;
            cfg.RightKey = fresh.RightKey;
            cfg.TapTimeoutMs = fresh.TapTimeoutMs;
            cfg.CancelOnMouse = fresh.CancelOnMouse;
            cfg.UseVkImeOnOff = fresh.UseVkImeOnOff;
            cfg.UseImeControlMessage = fresh.UseImeControlMessage;
            cfg.SuppressModifierSideEffect = fresh.SuppressModifierSideEffect;
            cfg.MaskKey = fresh.MaskKey;
            cfg.SwallowBoundKey = fresh.SwallowBoundKey;
            cfg.ShowImeStateInTray = fresh.ShowImeStateInTray;
            cfg.DebugLog = fresh.DebugLog;
            cfg.ShowIndicator = fresh.ShowIndicator;
            cfg.IndicatorSize = fresh.IndicatorSize;
            cfg.IndicatorHoldMs = fresh.IndicatorHoldMs;
            cfg.IndicatorFadeMs = fresh.IndicatorFadeMs;
            cfg.IndicatorPosition = fresh.IndicatorPosition;

            Log.Enabled = cfg.DebugLog;
            stateTimer.Enabled = cfg.ShowImeStateInTray;
            tray.ContextMenuStrip = BuildMenu();
            UpdateTooltip();
            Log.Write("設定を再読み込みした");
            tray.ShowBalloonTip(3000, AppInfo.Name, "設定を再読み込みしました。" + Summary(), ToolTipIcon.Info);
        }

        // --- Windows 側への登録 ---

        /// <summary>
        /// Windows 11 は通知領域のアイコンを既定で隠す。表示するかどうかは
        /// HKCU\Control Panel\NotifyIconSettings の IsPromoted で決まる。
        /// 自分の exe のエントリを探して切り替える。
        /// </summary>
        private static RegistryKey OpenNotifyIconKey(bool writable)
        {
            // Windows はパスの表記ごとに別の登録を作る（kanaei.exe と Kanaei.exe で別物になる）。
            // 綴りまで一致するものを優先し、無ければ大文字小文字を無視して拾う。
            RegistryKey fallback = null;
            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(AppInfo.NotifyIconSettings, false))
                {
                    if (root == null) return null;
                    foreach (string name in root.GetSubKeyNames())
                    {
                        RegistryKey sub = root.OpenSubKey(name, writable);
                        if (sub == null) continue;

                        string exe = sub.GetValue("ExecutablePath") as string;
                        if (exe != null && string.Equals(exe, AppInfo.ExePath, StringComparison.Ordinal))
                        {
                            if (fallback != null) fallback.Close();
                            return sub;
                        }
                        if (exe != null && fallback == null &&
                            string.Equals(exe, AppInfo.ExePath, StringComparison.OrdinalIgnoreCase))
                        {
                            fallback = sub;
                            continue;
                        }
                        sub.Close();
                    }
                }
            }
            catch { }
            return fallback;
        }

        private static bool IsPromoted()
        {
            using (RegistryKey k = OpenNotifyIconKey(false))
            {
                if (k == null) return false;
                object v = k.GetValue("IsPromoted");
                return v is int && (int)v != 0;
            }
        }

        private void SetPromoted(bool promote)
        {
            using (RegistryKey k = OpenNotifyIconKey(true))
            {
                if (k == null)
                {
                    MessageBox.Show(
                        "このアプリの通知領域の設定がまだ Windows に登録されていません。\n"
                      + "一度ログオンし直すか、タスクバーの「隠れているアイコン」から "
                      + AppInfo.Name + " をタスクバーへドラッグしてください。", AppInfo.Name);
                    miPromote.Checked = false;
                    return;
                }
                try { k.SetValue("IsPromoted", promote ? 1 : 0, RegistryValueKind.DWord); }
                catch (Exception ex)
                {
                    MessageBox.Show("タスクバーの表示設定に失敗しました。\n" + ex.Message, AppInfo.Name);
                    miPromote.Checked = IsPromoted();
                }
            }
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(AppInfo.RunKeyPath, false))
                {
                    if (k == null) return false;
                    return k.GetValue(AppInfo.RunValue) != null;
                }
            }
            catch { return false; }
        }

        private void SetStartup(bool enable)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(AppInfo.RunKeyPath, true))
                {
                    if (k == null) return;
                    // 旧名の登録が残っていたら消す
                    foreach (string old in AppInfo.LegacyRunValues) k.DeleteValue(old, false);
                    if (enable) k.SetValue(AppInfo.RunValue, "\"" + AppInfo.ExePath + "\"");
                    else k.DeleteValue(AppInfo.RunValue, false);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("スタートアップの設定に失敗しました。\n" + ex.Message, AppInfo.Name);
                miStartup.Checked = IsStartupEnabled();
            }
        }

        /// <summary>
        /// exe を消しただけでは残ってしまうもの（スタートアップ登録・タスクバー表示の設定・
        /// 設定ファイル・記録）をまとめて片付けて終了する。
        /// </summary>
        private void Uninstall()
        {
            string message =
                "次のものを削除します。\n\n"
              + "・スタートアップの登録\n"
              + "・タスクバー表示の設定\n"
              + "・設定ファイル: " + cfg.Path + "\n"
              + (Log.FilePath != null ? "・動作の記録: " + Log.FilePath + "\n" : "")
              + "\nキーの割り当ても消えます。よろしいですか？\n\n"
              + AppInfo.Id + ".exe 本体はお手数ですが手動で削除してください。";

            if (MessageBox.Show(message, AppInfo.Name, MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes)
                return;

            Log.Enabled = false;

            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(AppInfo.RunKeyPath, true))
                {
                    if (k != null)
                    {
                        k.DeleteValue(AppInfo.RunValue, false);
                        foreach (string old in AppInfo.LegacyRunValues) k.DeleteValue(old, false);
                    }
                }
            }
            catch { }

            try
            {
                using (RegistryKey root = Registry.CurrentUser.OpenSubKey(AppInfo.NotifyIconSettings, true))
                {
                    if (root != null)
                    {
                        foreach (string name in root.GetSubKeyNames())
                        {
                            string exe = null;
                            using (RegistryKey sub = root.OpenSubKey(name, false))
                            {
                                if (sub != null) exe = sub.GetValue("ExecutablePath") as string;
                            }
                            if (exe != null && string.Equals(exe, AppInfo.ExePath, StringComparison.OrdinalIgnoreCase))
                            {
                                root.DeleteSubKeyTree(name, false);
                                break;
                            }
                        }
                    }
                }
            }
            catch { }

            Delete(cfg.Path);
            if (Log.FilePath != null)
            {
                Delete(Log.FilePath);
                Delete(Log.FilePath + ".old");
            }

            tray.Visible = false;
            MessageBox.Show("登録と設定を削除しました。\n\n"
                + AppInfo.Id + ".exe 本体はお手数ですが手動で削除してください。", AppInfo.Name);
            ExitThread();
        }

        private static void Delete(string path)
        {
            try { if (path != null && File.Exists(path)) File.Delete(path); }
            catch { }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SystemEvents.PowerModeChanged -= OnPowerModeChanged;
                SystemEvents.SessionSwitch -= OnSessionSwitch;
                Log.Write("=== 終了 ===");
                if (cleanupTimer != null) cleanupTimer.Dispose();
                if (stateTimer != null) stateTimer.Dispose();
                if (hud != null) hud.Dispose();
                if (tray != null) { tray.Visible = false; tray.Dispose(); }
                if (hook != null) hook.Dispose();
                if (marshal != null) marshal.Dispose();
                if (iconEn != null) iconEn.Dispose();
                if (iconJa != null) iconJa.Dispose();
                if (iconOff != null) iconOff.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
