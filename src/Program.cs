using System;
using System.Threading;
using System.Windows.Forms;

namespace Kanaei
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            using (Mutex single = new Mutex(true, AppInfo.MutexName))
            {
                bool mine = single.WaitOne(TimeSpan.Zero, false);
                if (!mine)
                {
                    MessageBox.Show(AppInfo.Name + " はすでに起動しています（タスクトレイを確認してください）。",
                        AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);

                Config cfg = Config.Load(Config.DefaultPath());

                bool forceWizard = false;
                foreach (string a in args)
                {
                    if (a == "--assign" || a == "/assign" || a == "--wizard") forceWizard = true;
                }

                try
                {
                    if (!cfg.IsConfigured || forceWizard)
                    {
                        if (!RunSetup(cfg)) return;
                    }
                    Application.Run(new TrayApp(cfg));
                }
                catch (Exception ex)
                {
                    MessageBox.Show(AppInfo.Name + " の起動に失敗しました。\n\n" + ex.ToString(),
                        AppInfo.Name, MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
        }

        /// <summary>トレイを出す前に、割り当てウィザードだけを単独で回す。</summary>
        private static bool RunSetup(Config cfg)
        {
            using (Form marshal = new Form())
            {
                marshal.ShowInTaskbar = false;
                marshal.Opacity = 0;
                marshal.CreateControl();
                IntPtr h = marshal.Handle;
                GC.KeepAlive(h);

                using (KeyHook hook = new KeyHook(cfg, marshal))
                {
                    hook.Install();
                    using (WizardForm w = new WizardForm(hook, cfg))
                    {
                        return w.ShowDialog() == DialogResult.OK;
                    }
                }
            }
        }
    }
}
