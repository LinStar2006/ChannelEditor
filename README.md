# RGBA 通道编辑器（Paint.NET 5.1 插件）

在 Paint.NET 里把一张图的 R / G / B / A 四个通道**拆成独立图层**，像编辑普通图层那样
用画笔、填充、渐变、任意滤镜去改，改完再**合并回一张图**。

## 一键安装（推荐）

双击 `install.bat`。它会自己找到本机 Paint.NET 装在哪，然后装好插件。

它会依次检查：注册表卸载项 → 运行中的进程 → 文件关联 → 常见安装路径，都没找到就
扫描各个磁盘找 `paintdotnet.exe`；确认是 .NET 9 版（Paint.NET 5.1+）后，复制到
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

重新编译安装：

```powershell
$env:PATH = "<dotnet 安装目录>;" + $env:PATH
dotnet build ChannelEditor\ChannelEditor.csproj -c Release
# 先关掉 Paint.NET，否则 DLL 被占用
Copy-Item ChannelEditor\bin\Release\net9.0-windows\ChannelEditor.dll "<Paint.NET 安装目录>\Effects\ChannelEditor.dll" -Force
```

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
| `ChannelEditor.csproj` | net9.0-windows + unsafe |
