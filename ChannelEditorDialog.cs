using System;
using System.Drawing;
using System.Windows.Forms;
using PaintDotNet.Effects;

namespace ChannelEditor
{
    /// <summary>一个通道页里的全部控件引用。</summary>
    internal sealed class ChannelPageGui
    {
        public CheckBox Enabled;
        public NumericUpDown InBlack;
        public NumericUpDown InWhite;
        public NumericUpDown Gamma;
        public NumericUpDown OutBlack;
        public NumericUpDown OutWhite;
        public CheckBox Invert;
        public CheckBox UseConstant;
        public NumericUpDown Constant;
        public LutPreviewBox Preview;
        public Button Reset;

        /// <summary>是不是 A 通道那一页。A 页的「固定值」在界面上按 0-100 的百分比显示。</summary>
        public bool IsAlpha;
    }

    /// <summary>把 256 项查找表画成一条从左到右的灰度条带。</summary>
    internal sealed class LutPreviewBox : Control
    {
        public byte[] Lut = ChannelEditorEffect.CreateIdentityLut();

        public LutPreviewBox()
        {
            this.SetStyle(
                ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw,
                true);
            this.BackColor = Color.Black;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            Rectangle r = this.ClientRectangle;
            if (r.Width <= 0 || r.Height <= 0)
            {
                return;
            }

            byte[] lut = this.Lut ?? ChannelEditorEffect.CreateIdentityLut();
            int width = r.Width;

            using (SolidBrush brush = new SolidBrush(Color.Black))
            {
                for (int x = 0; x < width; x++)
                {
                    int index = width > 1 ? (int)((long)x * 255 / (width - 1)) : 0;
                    if (index < 0) index = 0;
                    else if (index > 255) index = 255;

                    int v = lut[index];
                    brush.Color = Color.FromArgb(v, v, v);
                    g.FillRectangle(brush, x, 0, 1, r.Height);
                }
            }

            using (Pen pen = new Pen(Color.FromArgb(128, 128, 128)))
            {
                g.DrawRectangle(pen, 0, 0, r.Width - 1, r.Height - 1);
            }
        }
    }

    /// <summary>
    /// RGBA 通道编辑器对话框：四个选项卡对应 R/G/B/A 四个通道，
    /// 每页可做输入色阶、Gamma、输出色阶、反转以及常量替换。
    /// </summary>
    internal sealed class ChannelEditorDialog : EffectConfigDialog
    {
        private static readonly string[] ChannelTitles = { "红 (R)", "绿 (G)", "蓝 (B)", "透明度 (A)" };

        private readonly ChannelPageGui[] pages = new ChannelPageGui[ChannelEditorToken.ChannelCount];
        private bool initializing;

        public ChannelEditorDialog()
        {
            this.BuildUi();
        }

        #region UI 构建

        private void BuildUi()
        {
            this.SuspendLayout();

            this.Text = "RGBA 通道编辑器";
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.ClientSize = new Size(600, 560);
            this.MinimumSize = new Size(560, 540);

            TabControl tabs = new TabControl();
            tabs.Dock = DockStyle.Fill;

            for (int i = 0; i < ChannelEditorToken.ChannelCount; i++)
            {
                TabPage page = new TabPage(ChannelTitles[i]);
                page.Padding = new Padding(0);
                page.AutoScroll = true;
                page.BackColor = SystemColors.Control;
                this.pages[i] = this.BuildChannelPage(page, i);
                tabs.TabPages.Add(page);
            }

            Panel bottom = new Panel();
            bottom.Dock = DockStyle.Bottom;
            bottom.Height = 54;

            FlowLayoutPanel buttons = new FlowLayoutPanel();
            buttons.Dock = DockStyle.Fill;
            buttons.FlowDirection = FlowDirection.RightToLeft;
            buttons.WrapContents = false;
            buttons.Padding = new Padding(16, 12, 16, 10);

            Button btnCancel = new Button();
            btnCancel.Text = "取消";
            btnCancel.Size = new Size(88, 30);
            btnCancel.Click += new EventHandler(this.OnCancelClick);

            Button btnOk = new Button();
            btnOk.Text = "确定";
            btnOk.Size = new Size(88, 30);
            btnOk.Click += new EventHandler(this.OnOkClick);

            buttons.Controls.Add(btnCancel);
            buttons.Controls.Add(btnOk);
            bottom.Controls.Add(buttons);

            this.AcceptButton = btnOk;
            this.CancelButton = btnCancel;

            // 先加填充区，再加底部栏：Dock 布局会先给底部栏留出位置。
            this.Controls.Add(tabs);
            this.Controls.Add(bottom);

            this.ResumeLayout(false);
        }

        private ChannelPageGui BuildChannelPage(TabPage page, int index)
        {
            ChannelPageGui gui = new ChannelPageGui();
            gui.IsAlpha = (index == ChannelEditorToken.ChannelA);
            EventHandler changed = new EventHandler(delegate { this.OnControlChanged(index); });

            const int left = 14;
            const int width = 530;

            // ① 启用开关
            CheckBox chkEnabled = new CheckBox();
            chkEnabled.Text = "处理该通道（取消勾选 = 保持原样）";
            chkEnabled.AutoSize = true;
            chkEnabled.Location = new Point(left, 12);
            chkEnabled.CheckedChanged += changed;
            page.Controls.Add(chkEnabled);
            gui.Enabled = chkEnabled;

            // ② 输入色阶
            GroupBox grpInput = new GroupBox();
            grpInput.Text = "输入色阶";
            grpInput.Location = new Point(left, 42);
            grpInput.Size = new Size(width, 100);
            page.Controls.Add(grpInput);

            AddLevelRow(grpInput, "输入黑点", 16, 26, 0, 255, 0, changed, out gui.InBlack);
            AddLevelRow(grpInput, "输入白点", 290, 26, 0, 255, 255, changed, out gui.InWhite);

            Label lblGamma = MakeLabel("Gamma", 16, 64, 70);
            grpInput.Controls.Add(lblGamma);

            NumericUpDown nudGamma = new NumericUpDown();
            nudGamma.Location = new Point(96, 60);
            nudGamma.Size = new Size(90, 24);
            nudGamma.DecimalPlaces = 2;
            nudGamma.Increment = 0.05m;
            nudGamma.Minimum = 0.10m;
            nudGamma.Maximum = 10.00m;
            nudGamma.Value = 1.00m;
            nudGamma.TextAlign = HorizontalAlignment.Right;
            nudGamma.ValueChanged += changed;
            grpInput.Controls.Add(nudGamma);
            gui.Gamma = nudGamma;

            Label lblGammaHint = MakeLabel(
                gui.IsAlpha
                    ? "输入色阶/Gamma 只对已有的半透明区域有效"
                    : "大于 1 提亮中间调，小于 1 压暗",
                196, 64, 320);
            lblGammaHint.ForeColor = SystemColors.GrayText;
            grpInput.Controls.Add(lblGammaHint);

            // ③ 输出色阶
            GroupBox grpOutput = new GroupBox();
            grpOutput.Text = "输出色阶";
            grpOutput.Location = new Point(left, 150);
            grpOutput.Size = new Size(width, 96);
            page.Controls.Add(grpOutput);

            AddLevelRow(grpOutput, "输出黑点", 16, 26, 0, 255, 0, changed, out gui.OutBlack);
            AddLevelRow(grpOutput, "输出白点", 290, 26, 0, 255, 255, changed, out gui.OutWhite);

            CheckBox chkInvert = new CheckBox();
            chkInvert.Text = "反转该通道输出";
            chkInvert.AutoSize = true;
            chkInvert.Location = new Point(18, 62);
            chkInvert.CheckedChanged += changed;
            grpOutput.Controls.Add(chkInvert);
            gui.Invert = chkInvert;

            // ④ 常量替换（A 页改成更直观的「整体不透明度」百分比）
            GroupBox grpConstant = new GroupBox();
            grpConstant.Text = gui.IsAlpha ? "整体不透明度" : "常量替换";
            grpConstant.Location = new Point(left, 254);
            grpConstant.Size = new Size(width, 94);
            page.Controls.Add(grpConstant);

            CheckBox chkUseConstant = new CheckBox();
            chkUseConstant.Text = gui.IsAlpha
                ? "忽略原值，把整个 A 通道统一设为下面的不透明度"
                : "忽略原值，整个通道统一输出下面的固定值";
            chkUseConstant.AutoSize = true;
            chkUseConstant.Location = new Point(18, 24);
            chkUseConstant.CheckedChanged += changed;
            grpConstant.Controls.Add(chkUseConstant);
            gui.UseConstant = chkUseConstant;

            Label lblConstant = MakeLabel(gui.IsAlpha ? "不透明度" : "固定值", 18, 58, 60);
            grpConstant.Controls.Add(lblConstant);

            NumericUpDown nudConstant = new NumericUpDown();
            nudConstant.Location = new Point(96, 56);
            nudConstant.Size = new Size(90, 24);
            nudConstant.Minimum = 0;
            nudConstant.Maximum = gui.IsAlpha ? 100 : 255;
            nudConstant.Value = gui.IsAlpha ? 100 : 255;
            nudConstant.TextAlign = HorizontalAlignment.Right;
            nudConstant.ValueChanged += changed;
            grpConstant.Controls.Add(nudConstant);
            gui.Constant = nudConstant;

            Label lblConstantHint = MakeLabel(
                gui.IsAlpha ? "0% 全透明 / 100% 全不透明" : "常用：Alpha 设为 0 全透明 / 255 全不透明",
                196, 58, 320);
            lblConstantHint.ForeColor = SystemColors.GrayText;
            grpConstant.Controls.Add(lblConstantHint);

            // ⑤ 映射预览
            Label lblPreview = MakeLabel("映射预览（左 → 右：输入值 0 → 255）", left, 354, 400);
            page.Controls.Add(lblPreview);

            LutPreviewBox preview = new LutPreviewBox();
            preview.Location = new Point(left, 378);
            preview.Size = new Size(width, 44);
            page.Controls.Add(preview);
            gui.Preview = preview;

            // ⑥ 重置
            Button btnReset = new Button();
            btnReset.Text = "重置此通道";
            btnReset.Location = new Point(left, 430);
            btnReset.Size = new Size(110, 30);
            btnReset.Click += new EventHandler(this.OnResetClick);
            btnReset.Tag = index;
            page.Controls.Add(btnReset);
            gui.Reset = btnReset;

            return gui;
        }

        private static Label MakeLabel(string text, int x, int y, int width)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = false;
            label.Location = new Point(x, y);
            label.Size = new Size(width, 22);
            label.TextAlign = ContentAlignment.MiddleLeft;
            return label;
        }

        private static void AddLevelRow(
            GroupBox parent,
            string caption,
            int labelX,
            int y,
            int min,
            int max,
            int value,
            EventHandler changed,
            out NumericUpDown result)
        {
            Label label = MakeLabel(caption, labelX, y + 2, 80);
            parent.Controls.Add(label);

            NumericUpDown nud = new NumericUpDown();
            nud.Location = new Point(labelX + 80, y);
            nud.Size = new Size(90, 24);
            nud.Minimum = min;
            nud.Maximum = max;
            nud.Value = value;
            nud.TextAlign = HorizontalAlignment.Right;
            nud.ValueChanged += changed;
            parent.Controls.Add(nud);

            result = nud;
        }

        #endregion

        #region 按钮事件

        private void OnOkClick(object sender, EventArgs e)
        {
            this.FinishTokenUpdate();
            this.DialogResult = DialogResult.OK;
        }

        private void OnCancelClick(object sender, EventArgs e)
        {
            this.DialogResult = DialogResult.Cancel;
        }

        private void OnResetClick(object sender, EventArgs e)
        {
            Button button = sender as Button;
            if (button == null || !(button.Tag is int))
            {
                return;
            }

            int index = (int)button.Tag;
            this.initializing = true;
            try
            {
                this.ResetPageControls(this.pages[index]);
            }
            finally
            {
                this.initializing = false;
            }

            this.UpdatePreview(index);
            this.FinishTokenUpdate();
        }

        #endregion

        #region token 同步

        protected override void InitialInitToken()
        {
            this.theEffectToken = new ChannelEditorToken();
        }

        protected override void InitDialogFromToken(EffectConfigToken effectToken)
        {
            ChannelEditorToken token = effectToken as ChannelEditorToken;
            if (token == null)
            {
                token = this.theEffectToken as ChannelEditorToken;
            }

            if (token == null || this.pages[0] == null)
            {
                return;
            }

            this.initializing = true;
            try
            {
                for (int i = 0; i < ChannelEditorToken.ChannelCount; i++)
                {
                    this.ReadPage(this.pages[i], token.Channels[i]);
                }
            }
            finally
            {
                this.initializing = false;
            }

            for (int i = 0; i < ChannelEditorToken.ChannelCount; i++)
            {
                this.UpdatePreview(i);
            }
        }

        protected override void InitTokenFromDialog()
        {
            ChannelEditorToken token = this.theEffectToken as ChannelEditorToken;
            if (token == null)
            {
                return;
            }

            for (int i = 0; i < ChannelEditorToken.ChannelCount; i++)
            {
                this.WritePage(this.pages[i], token.Channels[i]);
            }
        }

        #endregion

        #region 控件 <-> 参数

        private void ReadPage(ChannelPageGui gui, ChannelLevels levels)
        {
            gui.Enabled.Checked = levels.Enabled;
            SetNumeric(gui.InBlack, levels.InputBlack);
            SetNumeric(gui.InWhite, levels.InputWhite);
            SetNumeric(gui.Gamma, levels.Gamma);
            SetNumeric(gui.OutBlack, levels.OutputBlack);
            SetNumeric(gui.OutWhite, levels.OutputWhite);
            gui.Invert.Checked = levels.Invert;
            gui.UseConstant.Checked = levels.UseConstant;
            if (gui.IsAlpha)
            {
                // token 里存的是 0-255，A 页界面上显示 0-100 的百分比
                SetNumeric(gui.Constant, Math.Round(levels.Constant * 100.0 / 255.0));
            }
            else
            {
                SetNumeric(gui.Constant, levels.Constant);
            }
        }

        private void WritePage(ChannelPageGui gui, ChannelLevels levels)
        {
            levels.Enabled = gui.Enabled.Checked;
            levels.InputBlack = (int)gui.InBlack.Value;
            levels.InputWhite = (int)gui.InWhite.Value;
            levels.Gamma = (double)gui.Gamma.Value;
            levels.OutputBlack = (int)gui.OutBlack.Value;
            levels.OutputWhite = (int)gui.OutWhite.Value;
            levels.Invert = gui.Invert.Checked;
            levels.UseConstant = gui.UseConstant.Checked;
            if (gui.IsAlpha)
            {
                // A 页界面上是 0-100 的百分比，换回 0-255 存进 token
                levels.Constant = (int)Math.Round((double)gui.Constant.Value * 255.0 / 100.0);
            }
            else
            {
                levels.Constant = (int)gui.Constant.Value;
            }
        }

        private void ResetPageControls(ChannelPageGui gui)
        {
            gui.Enabled.Checked = true;
            SetNumeric(gui.InBlack, 0);
            SetNumeric(gui.InWhite, 255);
            SetNumeric(gui.Gamma, 1.0);
            SetNumeric(gui.OutBlack, 0);
            SetNumeric(gui.OutWhite, 255);
            gui.Invert.Checked = false;
            gui.UseConstant.Checked = false;
            SetNumeric(gui.Constant, gui.IsAlpha ? 100 : 255);
        }

        private static void SetNumeric(NumericUpDown nud, double value)
        {
            if (double.IsNaN(value) || double.IsInfinity(value))
            {
                value = (double)nud.Minimum;
            }

            double min = (double)nud.Minimum;
            double max = (double)nud.Maximum;
            if (value < min) value = min;
            if (value > max) value = max;

            nud.Value = (decimal)value;
        }

        #endregion

        #region 预览

        private void OnControlChanged(int index)
        {
            if (this.initializing)
            {
                return;
            }

            this.UpdatePreview(index);
            this.FinishTokenUpdate();
        }

        private void UpdatePreview(int index)
        {
            ChannelPageGui gui = this.pages[index];
            if (gui == null)
            {
                return;
            }

            ChannelLevels levels = new ChannelLevels();
            this.WritePage(gui, levels);

            gui.Preview.Lut = levels.BuildLut();
            gui.Preview.Invalidate();
        }

        #endregion
    }
}
