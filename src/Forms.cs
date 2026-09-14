using System;
using System.Drawing;
using System.Globalization;
using System.Windows.Forms;

namespace Kanaei
{
    /// <summary>キーを実際に押して割り当てを決めるウィザード。</summary>
    internal class WizardForm : Form
    {
        private readonly KeyHook hook;
        private readonly Config cfg;

        private readonly Label title = new Label();
        private readonly Label prompt = new Label();
        private readonly Label captured = new Label();
        private readonly Label note = new Label();
        private readonly Button retry = new Button();
        private readonly Button next = new Button();
        private readonly Button cancel = new Button();

        private int step;            // 0 = 英数キー, 1 = 日本語キー, 2 = 確認
        private int leftVk, rightVk;

        public WizardForm(KeyHook hook, Config cfg)
        {
            this.hook = hook;
            this.cfg = cfg;
            this.leftVk = cfg.LeftKey;
            this.rightVk = cfg.RightKey;

            Text = AppInfo.Name + " - キー割り当て";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(560, 330);
            Font = new Font("Yu Gothic UI", 9f);
            TopMost = true;
            try { Icon = AppInfo.Window(); } catch { }

            title.SetBounds(20, 16, 520, 24);
            title.Font = new Font("Yu Gothic UI", 12f, FontStyle.Bold);

            prompt.SetBounds(20, 48, 520, 44);

            captured.SetBounds(20, 96, 520, 34);
            captured.Font = new Font("Yu Gothic UI", 14f, FontStyle.Bold);
            captured.ForeColor = Color.FromArgb(0, 90, 158);

            note.SetBounds(20, 136, 520, 110);
            note.ForeColor = Color.FromArgb(90, 90, 90);

            retry.SetBounds(20, 262, 140, 30);
            retry.Text = "もう一度押す";
            retry.Click += delegate { StartCapture(); };

            next.SetBounds(300, 262, 120, 30);
            next.Text = "次へ";
            next.Click += delegate { Advance(); };

            cancel.SetBounds(428, 262, 110, 30);
            cancel.Text = "キャンセル";
            cancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };

            Controls.AddRange(new Control[] { title, prompt, captured, note, retry, next, cancel });

            // ウィザードにフォーカスがある間だけキーを横取りする。
            Activated += delegate { if (step < 2) StartCapture(); };
            Deactivate += delegate { hook.Capture = null; };
            FormClosed += delegate { hook.Capture = null; };

            ShowStep();
        }

        private void StartCapture()
        {
            captured.Text = "";
            note.Text = "";
            retry.Enabled = false;
            next.Enabled = false;
            prompt.Text = step == 0
                ? "英数入力（IME OFF）に切り替えたいキーを、今そのまま押してください。\n※ このウィンドウが前面にある間だけキーを横取りします。"
                : "日本語入力（IME ON）に切り替えたいキーを、今そのまま押してください。";
            hook.Capture = OnCaptured;
        }

        private bool OnCaptured(int vk, uint scan)
        {
            hook.Capture = null;

            if (step == 0) leftVk = vk; else rightVk = vk;

            captured.Text = KeyNames.Describe(vk);
            note.Text = KeyNames.InterferenceNote(vk);
            if (step == 1 && vk == leftVk)
            {
                note.Text = "左右に同じキーは割り当てられません。別のキーを押してください。";
                next.Enabled = false;
            }
            else
            {
                next.Enabled = true;
            }
            if (KeyNames.IsDangerous(vk))
                captured.ForeColor = Color.FromArgb(190, 40, 40);
            else
                captured.ForeColor = Color.FromArgb(0, 90, 158);

            retry.Enabled = true;
            return true;
        }

        private void Advance()
        {
            if (step < 2) { step++; ShowStep(); return; }

            cfg.LeftKey = leftVk;
            cfg.RightKey = rightVk;
            cfg.Save();
            DialogResult = DialogResult.OK;
            Close();
        }

        private void ShowStep()
        {
            hook.Capture = null;
            if (step == 0)
            {
                title.Text = "英数入力に切り替えるキー（1 / 2）";
                next.Text = "次へ";
                retry.Visible = true;
                StartCapture();
            }
            else if (step == 1)
            {
                title.Text = "日本語入力に切り替えるキー（2 / 2）";
                next.Text = "次へ";
                retry.Visible = true;
                StartCapture();
            }
            else
            {
                title.Text = "確認";
                retry.Visible = false;
                next.Text = "保存して有効化";
                next.Enabled = true;
                prompt.Text = "この割り当てで保存します。";
                captured.Text = KeyNames.Name(leftVk) + "  →  英数     /     " +
                                KeyNames.Name(rightVk) + "  →  日本語";
                captured.Font = new Font("Yu Gothic UI", 11f, FontStyle.Bold);
                captured.ForeColor = Color.FromArgb(0, 90, 158);
                note.Text = "設定ファイル: " + cfg.Path + "\n\n"
                          + "・割り当てたキーは他のキーと組み合わせて押した場合には切り替わりません。\n"
                          + "・うまく切り替わらないアプリがある場合は、設定ファイルの UseVkImeOnOff / "
                          + "UseImeControlMessage を片方ずつ 0 にして試してください。";
            }
        }
    }

    /// <summary>押したキーの仮想キーコードを確認するための窓。UHK など差し替え可能なキーボード向け。</summary>
    internal class InspectorForm : Form
    {
        private readonly KeyHook hook;
        private readonly ListBox list = new ListBox();
        private readonly Label head = new Label();

        public InspectorForm(KeyHook hook)
        {
            this.hook = hook;

            Text = AppInfo.Name + " - キーを調べる";
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 360);
            Font = new Font("Yu Gothic UI", 9f);
            TopMost = true;
            try { Icon = AppInfo.Window(); } catch { }

            head.SetBounds(12, 10, 496, 40);
            head.Text = "このウィンドウが前面にある間、押したキーの仮想キーコードを表示します。\n"
                      + "ここで押したキーはアプリには届きません。閉じると元に戻ります。";
            head.ForeColor = Color.FromArgb(90, 90, 90);

            list.SetBounds(12, 56, 496, 292);
            list.Font = new Font("Consolas", 10f);

            Controls.AddRange(new Control[] { head, list });

            Activated += delegate { hook.Capture = OnKey; };
            Deactivate += delegate { hook.Capture = null; };
            FormClosed += delegate { hook.Capture = null; };
        }

        private bool OnKey(int vk, uint scan)
        {
            string line = string.Format(CultureInfo.InvariantCulture,
                "VK 0x{0:X2} ({0,3})  scan 0x{1:X2}   {2}", vk, scan, KeyNames.Name(vk));
            list.Items.Insert(0, line);
            while (list.Items.Count > 200) list.Items.RemoveAt(list.Items.Count - 1);
            return true;
        }
    }
}
