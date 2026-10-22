using System;

namespace ChannelEditor
{
    /// <summary>
    /// 单个颜色通道的处理参数，以及把 [0,255] 输入映射为 [0,255] 输出的查找表（LUT）。
    /// </summary>
    [Serializable]
    public sealed class ChannelLevels : ICloneable
    {
        /// <summary>是否处理该通道。false 表示保持原值。</summary>
        public bool Enabled = true;

        /// <summary>输入黑点（0-255）。小于等于该值的像素被视为 0。</summary>
        public int InputBlack = 0;

        /// <summary>输入白点（0-255）。大于等于该值的像素被视为 255。</summary>
        public int InputWhite = 255;

        /// <summary>中间调 Gamma。大于 1 提亮（Photoshop 约定：pow(t, 1/gamma)）。</summary>
        public double Gamma = 1.0;

        /// <summary>输出黑点（0-255）。</summary>
        public int OutputBlack = 0;

        /// <summary>输出白点（0-255）。</summary>
        public int OutputWhite = 255;

        /// <summary>输出反转。</summary>
        public bool Invert = false;

        /// <summary>忽略原值，整个通道统一输出 Constant。</summary>
        public bool UseConstant = false;

        /// <summary>UseConstant 为 true 时输出的固定值（0-255）。</summary>
        public int Constant = 255;

        public ChannelLevels()
        {
        }

        private ChannelLevels(ChannelLevels other)
        {
            this.Enabled = other.Enabled;
            this.InputBlack = other.InputBlack;
            this.InputWhite = other.InputWhite;
            this.Gamma = other.Gamma;
            this.OutputBlack = other.OutputBlack;
            this.OutputWhite = other.OutputWhite;
            this.Invert = other.Invert;
            this.UseConstant = other.UseConstant;
            this.Constant = other.Constant;
        }

        public object Clone()
        {
            return new ChannelLevels(this);
        }

        /// <summary>把参数恢复为「恒等映射」。</summary>
        public void Reset()
        {
            this.Enabled = true;
            this.InputBlack = 0;
            this.InputWhite = 255;
            this.Gamma = 1.0;
            this.OutputBlack = 0;
            this.OutputWhite = 255;
            this.Invert = false;
            this.UseConstant = false;
            this.Constant = 255;
        }

        public bool IsIdentity
        {
            get
            {
                return this.Enabled
                    && !this.UseConstant
                    && !this.Invert
                    && this.InputBlack == 0
                    && this.InputWhite == 255
                    && this.OutputBlack == 0
                    && this.OutputWhite == 255
                    && Math.Abs(this.Gamma - 1.0) < 1e-9;
            }
        }

        /// <summary>
        /// 构建 256 项的映射表，lut[输入值] = 输出值。
        /// 运算顺序：输入色阶 → Gamma → 输出色阶 → 反转。
        /// </summary>
        public byte[] BuildLut()
        {
            byte[] lut = new byte[256];

            if (!this.Enabled)
            {
                for (int i = 0; i < 256; i++)
                {
                    lut[i] = (byte)i;
                }
                return lut;
            }

            if (this.UseConstant)
            {
                int constant = ClampToByte(this.Constant);
                for (int i = 0; i < 256; i++)
                {
                    lut[i] = (byte)constant;
                }
                return lut;
            }

            int inBlack = ClampToByte(this.InputBlack);
            int inWhite = ClampToByte(this.InputWhite);
            int outBlack = ClampToByte(this.OutputBlack);
            int outWhite = ClampToByte(this.OutputWhite);

            double inRange = inWhite - inBlack;
            if (inRange == 0)
            {
                inRange = 1; // 退化为硬阈值，避免除零
            }

            double gamma = this.Gamma;
            if (gamma <= 0.0 || double.IsNaN(gamma) || double.IsInfinity(gamma))
            {
                gamma = 1.0;
            }
            double exponent = 1.0 / gamma;

            for (int i = 0; i < 256; i++)
            {
                double t = (i - inBlack) / inRange;
                if (t < 0.0) t = 0.0;
                else if (t > 1.0) t = 1.0;

                if (exponent != 1.0)
                {
                    t = Math.Pow(t, exponent);
                }

                double v = outBlack + t * (outWhite - outBlack);
                if (this.Invert)
                {
                    v = 255.0 - v;
                }

                lut[i] = (byte)ClampToByte((int)Math.Round(v, MidpointRounding.AwayFromZero));
            }

            return lut;
        }

        private static int ClampToByte(int value)
        {
            if (value < 0) return 0;
            if (value > 255) return 255;
            return value;
        }
    }
}
