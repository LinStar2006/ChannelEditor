using System;
using System.Collections.Generic;
using System.Reflection;
using System.Windows.Forms;
using PaintDotNet;

namespace ChannelEditor
{
    /// <summary>
    /// 通道图层操作：把图像的 R/G/B/A 四个通道各自拆成一个灰度图层（像 Photoshop 的通道面板），
    /// 用户在普通图层上自由编辑（画笔、填充、渐变、滤镜、选区……）后再合并回彩色图像。
    ///
    /// 访问路径：Effect 插件拿不到文档服务（Paint.NET 5 的 ServiceProviderForEffects 不提供文档相关服务），
    /// 所以通过 Application.OpenForms 找到主窗口，再在控件树里找到 DocumentWorkspace，反射读取 Document。
    /// </summary>
    public static class ChannelLayerOps
    {
        public const int ChannelCount = 4;
        public const int ChannelR = 0;
        public const int ChannelG = 1;
        public const int ChannelB = 2;
        public const int ChannelA = 3;

        public static readonly string[] ChannelLayerNames = new string[]
        {
            "R 通道",
            "G 通道",
            "B 通道",
            "透明度 (A)"
        };

        public static readonly string[] ChannelShortNames = new string[] { "R", "G", "B", "A" };

        private const string MergedLayerName = "合并结果";

        /// <summary>
        /// 拆分时追加在四个通道图层之后的空占位图层。Paint.NET 图层面板会把“最后创建的那一层”
        /// 的缩略图显示成占位的文档合成图，让它顶住这个位置，通道图层的缩略图才会是正确灰度。
        /// </summary>
        public const string SpacerLayerName = "(通道占位层)";

        // ---------------------------------------------------------------
        // 文档访问（反射，避免编译期依赖 paintdotnet.dll 宿主程序集）
        // ---------------------------------------------------------------

        public static object FindDocumentWorkspace()
        {
            foreach (Form form in Application.OpenForms)
            {
                if (form.GetType().FullName == "PaintDotNet.Dialogs.MainForm")
                {
                    return FindChildByTypeName(form, "PaintDotNet.Controls.DocumentWorkspace");
                }
            }

            return null;
        }

        private static object FindChildByTypeName(Control root, string typeFullName)
        {
            Queue<Control> queue = new Queue<Control>();
            queue.Enqueue(root);
            int guard = 0;
            while (queue.Count > 0 && guard++ < 100000)
            {
                Control current = queue.Dequeue();
                if (current.GetType().FullName == typeFullName)
                {
                    return current;
                }

                foreach (Control child in current.Controls)
                {
                    queue.Enqueue(child);
                }
            }

            return null;
        }

        /// <summary>
        /// 让某个图层的缩略图重新生成。图层面板的缩略图由 LayersStrip 的 OurThumbnailProvider
        /// 用「添加图层那一刻」拿到的 layerRenderer 快照渲染，直接改 Surface 内容它不会察觉，
        /// 必须先 InvalidateThumbnail 丢掉这个 provider，再 RefreshLayerThumbnail 重建。
        /// </summary>
        public static void RefreshLayerThumbnail(Layer layer)
        {
            Control layersStrip = FindLayersStrip();
            if (layersStrip == null || layer == null)
            {
                return;
            }

            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            Type stripType = layersStrip.GetType();
            Type[] signature = new Type[] { typeof(Layer) };

            MethodInfo invalidate = stripType.GetMethod("InvalidateThumbnail", flags, null, signature, null);
            if (invalidate != null)
            {
                try
                {
                    invalidate.Invoke(layersStrip, new object[] { layer });
                }
                catch (Exception)
                {
                }
            }

            MethodInfo refresh = stripType.GetMethod("RefreshLayerThumbnail", flags, null, signature, null);
            if (refresh != null)
            {
                try
                {
                    refresh.Invoke(layersStrip, new object[] { layer });
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>把某个图层设为图层面板的“活动图层”。</summary>
        public static void SetActiveLayer(Layer layer)
        {
            Control layersStrip = FindLayersStrip();
            if (layersStrip == null || layer == null)
            {
                return;
            }

            PropertyInfo property = layersStrip.GetType().GetProperty("ActiveLayer");
            if (property != null)
            {
                try
                {
                    property.SetValue(layersStrip, layer);
                }
                catch (Exception)
                {
                }
            }
        }

        private static Control FindLayersStrip()
        {
            // 图层面板不一定挂在主窗口下面：5.x 里它默认是一个独立浮动的
            // PaintDotNet.Dialogs.LayersForm 窗口，只有停靠回主窗口时才在 MainForm 的控件树里。
            // 所以这里必须遍历所有打开的窗体逐一查找，只盯着 MainForm 会一直找不到。
            foreach (Form form in Application.OpenForms)
            {
                Control strip = FindChildByTypeName(form, "PaintDotNet.Controls.LayersStrip") as Control;
                if (strip != null)
                {
                    return strip;
                }
            }

            return null;
        }

        /// <summary>
        /// 让 Paint.NET 的图层面板重新生成所有图层缩略图。缩略图由 LayersStrip 交给后台
        /// ThumbnailManager 渲染并缓存，直接改 Surface 内容它不会自己察觉，必须显式刷新。
        /// </summary>
        public static void RefreshLayerPanel()
        {
            Control layersStrip = FindLayersStrip();
            if (layersStrip == null)
            {
                return;
            }

            BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
            MethodInfo refreshAll = layersStrip.GetType().GetMethod("RefreshAllLayerThumbnails", flags);
            if (refreshAll != null)
            {
                try
                {
                    refreshAll.Invoke(layersStrip, null);
                }
                catch (Exception)
                {
                }
            }
        }

        public static Document FindActiveDocument()
        {
            object workspace = FindDocumentWorkspace();
            if (workspace == null)
            {
                return null;
            }

            PropertyInfo documentProperty = workspace.GetType().GetProperty("Document");
            if (documentProperty == null)
            {
                return null;
            }

            return documentProperty.GetValue(workspace) as Document;
        }

        public static Layer FindActiveLayer()
        {
            object workspace = FindDocumentWorkspace();
            if (workspace == null)
            {
                return null;
            }

            PropertyInfo activeLayerProperty = workspace.GetType().GetProperty("ActiveLayer");
            if (activeLayerProperty == null)
            {
                return null;
            }

            return activeLayerProperty.GetValue(workspace) as Layer;
        }

        // ---------------------------------------------------------------
        // 查询
        // ---------------------------------------------------------------

        public static BitmapLayer FindChannelLayer(Document document, int channel)
        {
            string name = ChannelLayerNames[channel];
            foreach (Layer layer in document.Layers)
            {
                if (layer.Name == name)
                {
                    return layer as BitmapLayer;
                }
            }

            return null;
        }

        public static int CountChannelLayers(Document document)
        {
            int count = 0;
            for (int i = 0; i < ChannelCount; i++)
            {
                if (FindChannelLayer(document, i) != null)
                {
                    count++;
                }
            }

            return count;
        }

        private static bool IsChannelLayerName(string name)
        {
            // 占位图层也算“通道相关图层”：找原始图像时要跳过它，重复拆分前也要一并清理掉。
            if (name == SpacerLayerName)
            {
                return true;
            }

            for (int i = 0; i < ChannelLayerNames.Length; i++)
            {
                if (name == ChannelLayerNames[i])
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>找“原始图像”所在图层：自下而上第一个不是通道图层的普通像素图层。</summary>
        public static BitmapLayer FindBaseBitmapLayer(Document document)
        {
            for (int i = 0; i < document.Layers.Count; i++)
            {
                BitmapLayer bitmapLayer = document.Layers[i] as BitmapLayer;
                if (bitmapLayer != null && !IsChannelLayerName(bitmapLayer.Name))
                {
                    return bitmapLayer;
                }
            }

            return null;
        }

        // ---------------------------------------------------------------
        // 拆分
        // ---------------------------------------------------------------

        public static bool SplitChannels(Document document, bool fromActiveLayerOnly, out string message)
        {
            if (document == null)
            {
                message = "没有找到当前图像。";
                return false;
            }

            Surface source = null;
            bool ownSource = false;

            try
            {
                if (fromActiveLayerOnly)
                {
                    BitmapLayer activeLayer = FindActiveLayer() as BitmapLayer;
                    if (activeLayer == null)
                    {
                        message = "当前选中的不是普通像素图层（BitmapLayer），无法按图层拆分。请改选“整张图像”。";
                        return false;
                    }

                    if (activeLayer.Surface.Width != document.Width || activeLayer.Surface.Height != document.Height)
                    {
                        message = "当前图层尺寸与图像不一致，无法拆分。请改选“整张图像”。";
                        return false;
                    }

                    source = activeLayer.Surface;
                }
                else
                {
                    source = new Surface(document.Width, document.Height);
                    ownSource = true;
                    document.Flatten(source);
                }

                RemoveExistingChannelLayers(document);

                int width = document.Width;
                int height = document.Height;
                Surface[] channelSurfaces = new Surface[ChannelCount];
                for (int i = 0; i < ChannelCount; i++)
                {
                    channelSurfaces[i] = new Surface(width, height);
                }

                unsafe
                {
                    for (int y = 0; y < height; y++)
                    {
                        ColorBgra* sourceRow = source.GetRowPointer(y);
                        for (int x = 0; x < width; x++)
                        {
                            ColorBgra pixel = sourceRow[x];
                            channelSurfaces[ChannelR].GetRowPointer(y)[x] = ColorBgra.FromBgra(pixel.R, pixel.R, pixel.R, 255);
                            channelSurfaces[ChannelG].GetRowPointer(y)[x] = ColorBgra.FromBgra(pixel.G, pixel.G, pixel.G, 255);
                            channelSurfaces[ChannelB].GetRowPointer(y)[x] = ColorBgra.FromBgra(pixel.B, pixel.B, pixel.B, 255);
                            channelSurfaces[ChannelA].GetRowPointer(y)[x] = ColorBgra.FromBgra(pixel.A, pixel.A, pixel.A, 255);
                        }
                    }
                }

                // 逆序添加：图层是往后追加的，后加的在上层。
                // 逆序（A→B→G→R）加完之后，图层面板从上到下就是
                // (通道占位层) / R / G / B / A / 原图层，和 Photoshop 通道面板的顺序一致。
                for (int i = ChannelCount - 1; i >= 0; i--)
                {
                    BitmapLayer layer = new BitmapLayer(channelSurfaces[i], false);
                    layer.Name = ChannelLayerNames[i];
                    layer.Visible = false;
                    document.Layers.Add(layer);
                    layer.Invalidate();
                }

                // Paint.NET 的图层面板会把「最后创建的那一层」的缩略图显示成占位的文档合成图
                // （新增图层会自动成为图层面板的活动图层，也就是面板里高亮的那一行）。
                // 所以在四个通道图层之后再加一个空占位图层并留着它，让它去顶这个位置，
                // 通道图层的缩略图就都是各自正确的灰度内容了。
                AddSpacerLayer(document);

                document.Invalidate();

                message = "已创建 " + ChannelCount + " 个通道图层（默认隐藏）：R 通道 / G 通道 / B 通道 / 透明度 (A)。\r\n"
                    + "在图层面板中点亮某个通道图层即可看到它的灰度内容，然后用画笔、填充、渐变、滤镜等任意工具编辑它。\r\n"
                    + "编辑完成后重新打开本窗口，使用“合并通道图层”写回图像。";
                return true;
            }
            catch (Exception ex)
            {
                message = "拆分失败：" + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                if (ownSource && source != null)
                {
                    source.Dispose();
                }
            }
        }


        private static void RemoveExistingChannelLayers(Document document)
        {
            List<Layer> existing = new List<Layer>();
            foreach (Layer layer in document.Layers)
            {
                if (IsChannelLayerName(layer.Name))
                {
                    existing.Add(layer);
                }
            }

            for (int i = 0; i < existing.Count; i++)
            {
                document.Layers.Remove(existing[i]);
            }
        }

        /// <summary>
        /// 在文档最顶上放一个全透明、隐藏的占位图层，并把它作为「最后创建的那一层」。
        /// Paint.NET 的图层面板会把最后创建的那一层的缩略图显示成占位的文档合成图，
        /// 用它去顶这个位置，其它图层的缩略图才是各自真实的内容。
        /// 已经有占位层时先清掉，避免越积越多。
        /// </summary>
        private static void AddSpacerLayer(Document document)
        {
            for (int i = document.Layers.Count - 1; i >= 0; i--)
            {
                if (document.Layers[i].Name == SpacerLayerName)
                {
                    document.Layers.Remove(document.Layers[i]);
                }
            }

            BitmapLayer spacer = new BitmapLayer(document.Width, document.Height);
            spacer.Name = SpacerLayerName;
            spacer.Visible = false;
            document.Layers.Add(spacer);
        }

        // ---------------------------------------------------------------
        // 合并
        // ---------------------------------------------------------------

        public static bool MergeChannels(Document document, bool intoOriginalLayer, bool removeChannelLayers, out string message)
        {
            if (document == null)
            {
                message = "没有找到当前图像。";
                return false;
            }

            BitmapLayer[] channelLayers = new BitmapLayer[ChannelCount];
            int available = 0;
            for (int i = 0; i < ChannelCount; i++)
            {
                channelLayers[i] = FindChannelLayer(document, i);
                if (channelLayers[i] != null)
                {
                    available++;
                }
            }

            if (available == 0)
            {
                message = "没有找到通道图层。请先使用“拆分为通道图层”。";
                return false;
            }

            int width = document.Width;
            int height = document.Height;

            // 每个通道的像素来源：优先通道图层（尺寸匹配才用），否则回退到底图
            Surface[] channelSurfaces = new Surface[ChannelCount];
            for (int i = 0; i < ChannelCount; i++)
            {
                if (channelLayers[i] != null)
                {
                    Surface surface = channelLayers[i].Surface;
                    if (surface.Width == width && surface.Height == height)
                    {
                        channelSurfaces[i] = surface;
                    }
                }
            }

            BitmapLayer baseLayer = FindBaseBitmapLayer(document);
            Surface baseSurface = null;
            if (baseLayer != null && baseLayer.Surface.Width == width && baseLayer.Surface.Height == height)
            {
                baseSurface = baseLayer.Surface;
            }

            Surface result = new Surface(width, height);
            try
            {
                unsafe
                {
                    for (int y = 0; y < height; y++)
                    {
                        ColorBgra* destinationRow = result.GetRowPointer(y);
                        ColorBgra* baseRow = baseSurface == null ? null : baseSurface.GetRowPointer(y);
                        ColorBgra* rowR = channelSurfaces[ChannelR] == null ? null : channelSurfaces[ChannelR].GetRowPointer(y);
                        ColorBgra* rowG = channelSurfaces[ChannelG] == null ? null : channelSurfaces[ChannelG].GetRowPointer(y);
                        ColorBgra* rowB = channelSurfaces[ChannelB] == null ? null : channelSurfaces[ChannelB].GetRowPointer(y);
                        ColorBgra* rowA = channelSurfaces[ChannelA] == null ? null : channelSurfaces[ChannelA].GetRowPointer(y);

                        for (int x = 0; x < width; x++)
                        {
                            ColorBgra fallback = baseRow == null ? ColorBgra.FromBgra(0, 0, 0, 255) : baseRow[x];

                            byte valueR = rowR == null ? fallback.R : ToGrayValue(rowR[x]);
                            byte valueG = rowG == null ? fallback.G : ToGrayValue(rowG[x]);
                            byte valueB = rowB == null ? fallback.B : ToGrayValue(rowB[x]);
                            byte valueA = rowA == null ? fallback.A : ToGrayValue(rowA[x]);

                            destinationRow[x] = ColorBgra.FromBgra(valueB, valueG, valueR, valueA);
                        }
                    }
                }

                if (intoOriginalLayer && baseLayer != null)
                {
                    CopySurface(result, baseLayer.Surface);
                    baseLayer.Invalidate();
                }
                else
                {
                    BitmapLayer merged = new BitmapLayer(result, true);
                    result = null; // 所有权交给新图层
                    merged.Name = MergedLayerName;
                    document.Layers.Add(merged);
                }

                // 合并完先把通道图层全部隐藏：用户如果点亮过某个通道图层去编辑，它还盖在
                // 合并结果上面，画布看起来就是那张通道灰度图，很容易误以为合并没有生效。
                for (int i = 0; i < ChannelCount; i++)
                {
                    if (channelLayers[i] != null)
                    {
                        channelLayers[i].Visible = false;
                    }
                }

                if (removeChannelLayers)
                {
                    // 连同旧的“通道占位层”一起清掉，稍后按需重新放一个。
                    RemoveExistingChannelLayers(document);
                }

                // 生成新图层时，新图层同样是「最后创建的那一层」，缩略图会显示成占位的合成图。
                // 和拆分时一样，在它上面补一个占位图层，让合并结果不是最后创建的那一层。
                if (!intoOriginalLayer)
                {
                    AddSpacerLayer(document);
                }

                document.Invalidate();

                message = "已把 " + available + " 个通道图层合并"
                    + (intoOriginalLayer && baseLayer != null ? "并覆盖到图层“" + baseLayer.Name + "”。" : "为新图层“" + MergedLayerName + "”。")
                    + (removeChannelLayers ? "\r\n四个通道图层已删除。" : "\r\n四个通道图层已保留。");
                return true;
            }
            catch (Exception ex)
            {
                message = "合并失败：" + ex.GetType().Name + ": " + ex.Message;
                return false;
            }
            finally
            {
                if (result != null)
                {
                    result.Dispose();
                }
            }
        }

        /// <summary>把灰度图层的像素折算成单通道值（兼容用户在灰度图层上画了彩色）。</summary>
        private static byte ToGrayValue(ColorBgra pixel)
        {
            int luminance = (pixel.R * 299 + pixel.G * 587 + pixel.B * 114) / 1000;
            return (byte)(luminance < 0 ? 0 : (luminance > 255 ? 255 : luminance));
        }

        private static unsafe void CopySurface(Surface source, Surface destination)
        {
            int width = Math.Min(source.Width, destination.Width);
            int height = Math.Min(source.Height, destination.Height);
            for (int y = 0; y < height; y++)
            {
                ColorBgra* sourceRow = source.GetRowPointer(y);
                ColorBgra* destinationRow = destination.GetRowPointer(y);
                for (int x = 0; x < width; x++)
                {
                    destinationRow[x] = sourceRow[x];
                }
            }
        }

        // ---------------------------------------------------------------
        // 状态描述
        // ---------------------------------------------------------------

        public static string DescribeChannelState(Document document, int channel)
        {
            if (document == null)
            {
                return "（没有打开的图像）";
            }

            BitmapLayer layer = FindChannelLayer(document, channel);
            if (layer == null)
            {
                return "未创建";
            }

            return (layer.Visible ? "显示中" : "已隐藏")
                + "，" + layer.Surface.Width + "×" + layer.Surface.Height
                + "，不透明度 " + layer.Opacity;
        }
    }
}