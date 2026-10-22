using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;
using PaintDotNet;

namespace ChannelEditor
{
    /// <summary>
    /// 在 Paint.NET 主窗口的工具栏上挂两个按钮：“拆分通道”和“合并通道”，
    /// 点一下直接按默认参数执行，不再弹配置对话框。
    ///
    /// 为什么要用后台线程轮询：Paint.NET 的插件 API 只提供“特效”菜单入口，没有工具栏
    /// 扩展点。主窗口是普通 WinForms 窗体，工具栏就是 ToolStrip 控件，插件可以在启动后
    /// 通过反射拿到它并往上加按钮。效果类被实例化时主窗体还没创建（Application.OpenForms
    /// 为空），而 Paint.NET 的启动流程既不触发 Application.Idle，WinForms 定时器也还没
    /// 开始泵消息，所以只能用 System.Threading.Timer 轮询，等主窗体就绪后用 BeginInvoke
    /// 回到 UI 线程注入。
    /// </summary>
    internal static class ChannelToolbar
    {
        private const string SplitButtonName = "ChannelEditorSplitButton";
        private const string MergeButtonName = "ChannelEditorMergeButton";

        private static System.Threading.Timer pollTimer;
        private static int pollAttempt;
        private static bool installed;

        internal static void Log(string text)
        {
            try
            {
                System.IO.File.AppendAllText(
                    System.IO.Path.Combine(System.IO.Path.GetTempPath(), "channel_toolbar_debug.txt"),
                    DateTime.Now.ToString("HH:mm:ss.fff") + " " + text + "\r\n");
            }
            catch
            {
            }
        }

        /// <summary>由效果类在构造时调用；重复调用无副作用。</summary>
        public static void Install()
        {
            if (installed || pollTimer != null)
            {
                return;
            }

            Log("Install() called, openForms=" + Application.OpenForms.Count);
            pollTimer = new System.Threading.Timer(OnPoll, null, 300, 300);
        }

        private static void OnPoll(object state)
        {
            if (installed)
            {
                StopTimer();
                return;
            }

            try
            {
                pollAttempt++;
                if (pollAttempt <= 3 || pollAttempt % 20 == 0)
                {
                    Log("poll #" + pollAttempt + " openForms=" + Application.OpenForms.Count);
                }

                Form mainForm = FindMainForm();
                if (mainForm == null || !mainForm.IsHandleCreated || !mainForm.Visible)
                {
                    return;
                }

                Log("mainForm found at poll #" + pollAttempt);
                StopTimer();

                mainForm.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        InjectOnUiThread(mainForm);
                    }
                    catch (Exception ex)
                    {
                        Log("inject failed: " + ex.GetType().Name + ": " + ex.Message);
                    }
                }));
            }
            catch (Exception ex)
            {
                Log("poll error: " + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void StopTimer()
        {
            System.Threading.Timer timer = pollTimer;
            pollTimer = null;
            if (timer != null)
            {
                timer.Dispose();
            }
        }

        private static void InjectOnUiThread(Form mainForm)
        {
            ToolStrip auxMenu = FindAuxMenu(mainForm);
            if (auxMenu == null)
            {
                Log("aux menu not found");
                return;
            }

            BuildButtons(auxMenu);
            installed = true;
            Log("injected into " + auxMenu.GetType().FullName + " items=" + auxMenu.Items.Count);
        }

        /// <summary>
        /// 找窗口右上角那排小图标（锤子=工具 / 历史 / 图层 / 颜色 / 设置 / 帮助），
        /// 真实类型是 PaintDotNet.Menus.PdnAuxMenu（PdnMenuStrip 的子类）。
        /// </summary>
        private static ToolStrip FindAuxMenu(Control root)
        {
            if (root == null)
            {
                return null;
            }

            if (root.GetType().FullName == "PaintDotNet.Menus.PdnAuxMenu")
            {
                return root as ToolStrip;
            }

            foreach (Control child in root.Controls)
            {
                ToolStrip found = FindAuxMenu(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static Form FindMainForm()
        {
            FormCollection forms = Application.OpenForms;
            for (int i = 0; i < forms.Count; i++)
            {
                Form form = forms[i];
                if (form != null && form.GetType().FullName == "PaintDotNet.Dialogs.MainForm")
                {
                    return form;
                }
            }

            return null;
        }

        private static void DumpControls(Control root)
        {
            if (root == null)
            {
                return;
            }

            ToolStrip strip = root as ToolStrip;
            if (strip != null)
            {
                string head = "";
                int count = Math.Min(strip.Items.Count, 40);
                for (int i = 0; i < count; i++)
                {
                    ToolStripItem item = strip.Items[i];
                    head += "[" + item.GetType().Name + " tip='" + item.ToolTipText + "' text='" + item.Text + "']";
                }

                Log("  strip type=" + strip.GetType().FullName + " items=" + strip.Items.Count
                    + " dock=" + strip.Dock + " h=" + strip.Height + " " + head);
            }

            foreach (Control child in root.Controls)
            {
                DumpControls(child);
            }
        }

        /// <summary>找主工具栏：带“撤销”提示的那个 ToolStrip（工具选项栏没有这个）。</summary>
        private static ToolStrip FindMainToolStrip(Control root)
        {
            ToolStrip strip = root as ToolStrip;
            if (strip != null && strip.Items.Count >= 8 && ContainsUndoItem(strip))
            {
                return strip;
            }

            foreach (Control child in root.Controls)
            {
                ToolStrip found = FindMainToolStrip(child);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static bool ContainsUndoItem(ToolStrip strip)
        {
            foreach (ToolStripItem item in strip.Items)
            {
                string tip = item.ToolTipText;
                if (string.IsNullOrEmpty(tip))
                {
                    continue;
                }

                if (tip.Contains("撤销") || tip.Contains("Undo"))
                {
                    return true;
                }
            }

            return false;
        }

        private static void BuildButtons(ToolStrip strip)
        {
            if (strip.Items.ContainsKey(SplitButtonName))
            {
                return;
            }

            // 插在“工具 (F5)”那个锤子图标左边；找不到就放在最前面。
            // 这个 ToolStrip 是 PaintDotNet.Menus.PdnAuxMenu（PdnMenuStrip 的子类），
            // 它没有重写 OnItemClicked，所以这里可以放心用 ToolStripButton。
            // （主工具栏 CommonActionsStrip 就不行：它会对 ToolStripButton 强制把 Tag
            //   转成 PaintDotNet.CommonAction，Tag 为 null 会抛异常把 Paint.NET 崩掉。）
            // 那排是 16x16 图标按钮，所以这里也用图标，宽度才和邻居一致。
            int insertAt = 0;
            for (int i = 0; i < strip.Items.Count; i++)
            {
                string tip = strip.Items[i].ToolTipText;
                if (!string.IsNullOrEmpty(tip) && tip.StartsWith("工具"))
                {
                    insertAt = i;
                    break;
                }
            }

            ToolStripButton splitButton = new ToolStripButton
            {
                Name = SplitButtonName,
                Image = MakeSplitIcon(),
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                ToolTipText = "拆分通道：把图像拆分为 R / G / B / A 四个通道图层（取整张图像）",
            };
            splitButton.Click += OnSplitClicked;

            ToolStripButton mergeButton = new ToolStripButton
            {
                Name = MergeButtonName,
                Image = MakeMergeIcon(),
                DisplayStyle = ToolStripItemDisplayStyle.Image,
                ToolTipText = "合并通道：把通道图层合并成新图层，并删除四个通道图层",
            };
            mergeButton.Click += OnMergeClicked;

            strip.Items.Insert(insertAt, splitButton);
            strip.Items.Insert(insertAt + 1, mergeButton);
            strip.PerformLayout();
        }

        /// <summary>拆分图标：2x2 四色小方块，代表 R / G / B / A 拆开。</summary>
        private static Image MakeSplitIcon()
        {
            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                g.FillRectangle(Brushes.Red, 1, 1, 6, 6);
                g.FillRectangle(Brushes.LimeGreen, 9, 1, 6, 6);
                g.FillRectangle(Brushes.DodgerBlue, 1, 9, 6, 6);
                g.FillRectangle(Brushes.DimGray, 9, 9, 6, 6);
                g.DrawRectangle(Pens.Black, 0, 0, 15, 15);
            }

            return bmp;
        }

        /// <summary>合并图标：四色斜向渐变合成一块，代表通道合回一张图。</summary>
        private static Image MakeMergeIcon()
        {
            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    new Rectangle(1, 1, 14, 14), Color.Red, Color.DodgerBlue, 45f))
                {
                    g.FillRectangle(brush, 1, 1, 14, 14);
                }

                g.DrawRectangle(Pens.Black, 0, 0, 15, 15);
            }

            return bmp;
        }

        private static void OnSplitClicked(object sender, EventArgs e)
        {
            try
            {
                Document document = ChannelLayerOps.FindActiveDocument();
                if (document == null)
                {
                    ShowWarning("没有打开的图像。");
                    return;
                }

                bool ok = ChannelLayerOps.SplitChannels(document, false, out string message);
                document.Invalidate();
                ChannelLayerOps.RefreshLayerPanel();

                if (!ok)
                {
                    ShowWarning(message);
                }
            }
            catch (Exception ex)
            {
                ShowWarning("拆分失败：" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void OnMergeClicked(object sender, EventArgs e)
        {
            try
            {
                Document document = ChannelLayerOps.FindActiveDocument();
                if (document == null)
                {
                    ShowWarning("没有打开的图像。");
                    return;
                }

                // 默认配置：生成一个新图层（不破坏原图），合并后删除四个通道图层
                bool ok = ChannelLayerOps.MergeChannels(document, false, true, out string message);
                document.Invalidate();
                ChannelLayerOps.RefreshLayerPanel();

                if (!ok)
                {
                    ShowWarning(message);
                }
            }
            catch (Exception ex)
            {
                ShowWarning("合并失败：" + ex.GetType().Name + ": " + ex.Message);
            }
        }

        private static void ShowWarning(string text)
        {
            MessageBox.Show(text, "RGBA 通道", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
    }
}
