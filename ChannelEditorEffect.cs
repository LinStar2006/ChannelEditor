using System;
using System.Drawing;
using PaintDotNet;
using PaintDotNet.Effects;

namespace ChannelEditor
{
    /// <summary>
    /// RGBA 通道色阶：用一个自定义 WinForms 对话框，让用户分别对 R、G、B、A 四个通道
    /// 做输入色阶 / Gamma / 输出色阶 / 反转 / 常量替换。
    /// （通道图层的拆合编辑见 ChannelLayerEffect。）
    /// </summary>
    public sealed class ChannelEditorEffect : Effect
    {
        public const string EffectName = "RGBA 通道色阶";

        // 四张查找表，由 OnSetRenderInfo 在每帧渲染前重建，Render 期间只读。
        private byte[] lutR = CreateIdentityLut();
        private byte[] lutG = CreateIdentityLut();
        private byte[] lutB = CreateIdentityLut();
        private byte[] lutA = CreateIdentityLut();

        public ChannelEditorEffect()
            : base(
                  EffectName,
                  SubmenuNames.Color,
                  new EffectOptions { Flags = EffectFlags.Configurable })
        {
            // Paint.NET 启动时会实例化所有效果类，借用这个时机往主窗口工具栏挂
            // “拆分通道 / 合并通道”两个按钮（插件 API 本身没有工具栏扩展点）。
            ChannelToolbar.Install();
        }

        public override EffectConfigDialog CreateConfigDialog()
        {
            return new ChannelEditorDialog();
        }

        protected override void OnSetRenderInfo(EffectConfigToken newToken, RenderArgs dstArgs, RenderArgs srcArgs)
        {
            base.OnSetRenderInfo(newToken, dstArgs, srcArgs);

            ChannelEditorToken token = newToken as ChannelEditorToken;
            if (token == null)
            {
                this.lutR = CreateIdentityLut();
                this.lutG = CreateIdentityLut();
                this.lutB = CreateIdentityLut();
                this.lutA = CreateIdentityLut();
                return;
            }

            // 先建好再整体替换，避免其他渲染线程读到半成品。
            byte[] r = token.Channels[ChannelEditorToken.ChannelR].BuildLut();
            byte[] g = token.Channels[ChannelEditorToken.ChannelG].BuildLut();
            byte[] b = token.Channels[ChannelEditorToken.ChannelB].BuildLut();
            byte[] a = token.Channels[ChannelEditorToken.ChannelA].BuildLut();

            this.lutR = r;
            this.lutG = g;
            this.lutB = b;
            this.lutA = a;
        }

        public override void Render(
            EffectConfigToken parameters,
            RenderArgs dstArgs,
            RenderArgs srcArgs,
            Rectangle[] rois,
            int startIndex,
            int length)
        {
            byte[] lutR = this.lutR;
            byte[] lutG = this.lutG;
            byte[] lutB = this.lutB;
            byte[] lutA = this.lutA;

            // 兜底：极端情况下（未经 OnSetRenderInfo）也无所谓，用恒等表。
            if (lutR == null) lutR = CreateIdentityLut();
            if (lutG == null) lutG = CreateIdentityLut();
            if (lutB == null) lutB = CreateIdentityLut();
            if (lutA == null) lutA = CreateIdentityLut();

            Surface dst = dstArgs.Surface;
            Surface src = srcArgs.Surface;

            unsafe
            {
                for (int roiIndex = startIndex; roiIndex < startIndex + length; roiIndex++)
                {
                    Rectangle rect = rois[roiIndex];

                    for (int y = rect.Top; y < rect.Bottom; y++)
                    {
                        ColorBgra* srcRow = src.GetRowPointer(y);
                        ColorBgra* dstRow = dst.GetRowPointer(y);

                        for (int x = rect.Left; x < rect.Right; x++)
                        {
                            ColorBgra c = srcRow[x];
                            c.B = lutB[c.B];
                            c.G = lutG[c.G];
                            c.R = lutR[c.R];
                            c.A = lutA[c.A];
                            dstRow[x] = c;
                        }
                    }
                }
            }
        }

        internal static byte[] CreateIdentityLut()
        {
            byte[] lut = new byte[256];
            for (int i = 0; i < 256; i++)
            {
                lut[i] = (byte)i;
            }
            return lut;
        }
    }
}
