# RGBA 通道编辑器（Paint.NET 5.1 / 5.2 插件）

在 Paint.NET 里把一张图的 R / G / B / A 四个通道**拆成独立图层**，像编辑普通图层那样
用画笔、填充、渐变、任意滤镜去改，改完再**合并回一张图**。

## 一键安装（推荐）

双击 `install.bat`。它会自己找到本机 Paint.NET 装在哪，然后装好插件。

它会依次检查：注册表卸载项 → 运行中的进程 → 文件关联 → 常见安装路径，都没找到就
扫描各个磁盘找 `paintdotnet.exe`；确认是 .NET 9+ 版（Paint.NET 5.1+）后，复制到
`<Paint.NET 目录>\Effects\`，目录不可写时退回 `%LOCALAPPDATA%\paint.net\Effects\`，
并删掉另一处可能重复加载的同名 DLL。

Paint.NET 开着的话脚本会先问你要不要关掉它。

```
install.bat -Uninstall                        卸载
install.bat -PaintNetDir "D:\App\Paint.NET"    手动指定目录
```

## 手动安装

把 `ChannelEditor.dll` 复制到 Paint.NET 的插件目录，重启 Paint.NET：

```
%LOCALAPPDATA%\paint.net\Effects\ChannelEditor.dll
```

（或者 Paint.NET 程序目录下的 `Effects\`）

## 从源码编译

`ChannelEditor.csproj` 里每个 `<Reference>` 的 `HintPath` 都指向 Paint.NET 安装目录下的
`PaintDotNet.*.dll`，按你本机的路径改。

```powershell
dotnet build ChannelEditor\ChannelEditor.csproj -c Release
# 先关掉 Paint.NET，否则 DLL 被占用
# 产物在 ChannelEditor\bin\Release\<TargetFramework>\ChannelEditor.dll
Copy-Item ChannelEditor\bin\Release\<TargetFramework>\ChannelEditor.dll "<Paint.NET 安装目录>\Effects\ChannelEditor.dll" -Force
```

- **Paint.NET 5.1**：`<TargetFramework>` 用 `net9.0-windows`，引用 5.1 的 `PaintDotNet.*.dll`，可以直接编译。
- **Paint.NET 5.2**：经典效果 API（`Effect` / `EffectConfigDialog`）被标成 **error 级**
  `[Obsolete]`，编译报 `CS0619`，`#pragma warning disable` 和 `NoWarn` 都压不住；而且 5.2 的
  `PaintDotNet.*.dll` 依赖 `System.Runtime 11.0.0.0`，必须配 `net11.0-windows`。所以**源码不能
  直接用 5.2 的引用程序集编译**。

已发布的 DLL 是在 5.1 引用程序集 + `net9.0-windows` 下编译，再用 Mono.Cecil 打两个 IL 补丁
得到的（`Layer.Invalidate()` 在 5.2 变成 internal、`SetToolFromType` 被改名成 `SetTool`），
5.1 和 5.2 都能加载。

## 用法

重启后，窗口右上角那排小图标里、**锤子（工具 F5）左边**会多出两个按钮：

```
[四色方块] [渐变方块] 🔨 🕐 📋 🎨 ⚙ ❓
```

第一个是**拆分通道**（2×2 四色方块图标），第二个是**合并通道**（渐变色方块图标），鼠标悬停有提示。

- **拆分通道**：把当前图像拆成 R / G / B / A 四个灰度图层（取整张图像合成后的结果），
  并在最顶上放一个隐藏的 `(通道占位层)`
- **合并通道**：把四个通道图层合并成一个新图层「合并结果」（不破坏原图），
  然后删掉四个通道图层，最顶上同样留一个 `(通道占位层)`

点一下直接执行，不再弹配置对话框。

### 完整流程

1. 打开图片 → 点 **拆分通道**
2. 在图层面板里点某个通道图层（比如 `R 通道`），勾选它的对号
3. 用画笔 / 填充 / 渐变 / 滤镜随便改这个灰度图
4. 改完点 **合并通道** → 生成「合并结果」，四个通道图层自动删除
5. 需要的话把「合并结果」向下合并进「背景」，或直接保存

拆分后图层面板从上到下是：

```
(通道占位层)   ← 隐藏
R 通道         ← 隐藏
G 通道         ← 隐藏
B 通道         ← 隐藏
透明度 (A)     ← 隐藏
原图层         ← 可见
```

和 Photoshop 的通道面板顺序一致（R 在最上面，A 在最下面）。

### 关于 `(通道占位层)`

拆分和合并都会在最顶上留一个全透明、隐藏的 `(通道占位层)`。

原因是 Paint.NET 图层面板对「最后创建的那一层」有缓存问题：新增图层会自动成为活动图层，
而活动图层的缩略图在刷新前显示的是占位的文档合成图。占位层替它挡下这个位置，
下面的通道图层 / 合并结果就都能显示正确的缩略图了。

### 通道值怎么折算

拆分时每个通道的灰度值就是该通道的原始值：

| 图层 | 灰度 = |
|---|---|
| `R 通道` | 像素的 R |
| `G 通道` | 像素的 G |
| `B 通道` | 像素的 B |
| `透明度 (A)` | 像素的 A（255 = 不透明，0 = 全透明） |

合并时反过来：四个通道图层同一点的灰度值分别写回 R / G / B / A。某个通道图层
被删掉了，就保留原图那个通道的值。

### 彩色像素怎么折算

通道图层本该是灰度图，但如果你用彩色画笔、渐变、滤镜在上面涂了颜色，合并时会先把
彩色像素折算成一个灰度值，用的是 **Rec.601 亮度公式**：

```
灰度 = (R × 299 + G × 587 + B × 114) ÷ 1000
```

也就是按人眼感知加权：红 29.9%、绿 58.7%、蓝 11.4%。几个例子：

| 涂的颜色 | 折算出的通道值 |
|---|---|
| 纯红 `(255,0,0)` | 76 |
| 纯绿 `(0,255,0)` | 150 |
| 纯蓝 `(0,0,255)` | 29 |
| 黄 `(255,255,0)` | 226 |
| 白 `(255,255,255)` | 255 |
| 黑 `(0,0,0)` | 0 |

两个要点：

- **完全不看透明度**：读的是图层 Surface 里的原始像素，像素的 alpha 和图层的不透明度
  都不参与计算。半透明的纯红照样算 76。
- 纯灰度像素（R=G=B）折算前后完全一样，所以正常用灰度图编辑时这条规则不起作用。

### 还保留的菜单入口

`特效 > 颜色 > RGBA 通道色阶` 是另一个独立功能：用色阶 / Gamma / 常量替换的方式
批量调整四个通道（不建图层），需要精细参数时可以用它。

## 源码

| 文件 | 职责 |
|---|---|
| `ChannelToolbar.cs` | 往主工具栏注入「拆分通道」「合并通道」两个按钮 |
| `ChannelLayerOps.cs` | 拆分 / 合并的核心逻辑，以及反射拿 `Document` 的入口 |
| `ChannelEditorEffect.cs` | 「RGBA 通道色阶」效果（保留的菜单入口，也是 DLL 被加载的触发点） |
| `ChannelEditorDialog.cs` | 色阶效果的配置对话框（4 个标签页 + 映射预览条） |
| `ChannelEditorToken.cs` | 色阶效果的参数 token |
| `ChannelLevels.cs` | 单通道的色阶参数与 256 项 LUT 生成 |
| `ChannelEditor.csproj` | `net11.0-windows` + unsafe，引用本机 Paint.NET 的 `PaintDotNet.*.dll` |
