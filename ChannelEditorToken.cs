using System;
using PaintDotNet.Effects;

namespace ChannelEditor
{
    /// <summary>
    /// 插件参数：四个通道（R、G、B、A）各一份独立的 ChannelLevels。
    /// 索引约定：0 = R，1 = G，2 = B，3 = A。
    /// </summary>
    [Serializable]
    public sealed class ChannelEditorToken : EffectConfigToken
    {
        public const int ChannelCount = 4;
        public const int ChannelR = 0;
        public const int ChannelG = 1;
        public const int ChannelB = 2;
        public const int ChannelA = 3;

        public readonly ChannelLevels[] Channels;

        public ChannelEditorToken()
        {
            this.Channels = new ChannelLevels[ChannelCount];
            for (int i = 0; i < ChannelCount; i++)
            {
                this.Channels[i] = new ChannelLevels();
            }
        }

        private ChannelEditorToken(ChannelEditorToken other)
        {
            this.Channels = new ChannelLevels[ChannelCount];
            for (int i = 0; i < ChannelCount; i++)
            {
                this.Channels[i] = (ChannelLevels)other.Channels[i].Clone();
            }
        }

        public override object Clone()
        {
            return new ChannelEditorToken(this);
        }

        /// <summary>取消或改动的通道是否需要重绘（全部恒等时依然照常输出，保证行为简单）。</summary>
        public bool HasChanges
        {
            get
            {
                for (int i = 0; i < ChannelCount; i++)
                {
                    if (!this.Channels[i].IsIdentity)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        public void Reset()
        {
            for (int i = 0; i < ChannelCount; i++)
            {
                this.Channels[i].Reset();
            }
        }
    }
}
