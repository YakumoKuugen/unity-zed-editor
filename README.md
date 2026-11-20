# Zed Editor — Unity 集成

为 Unity 编辑器提供 [Zed](https://zed.dev) 代码编辑器集成，参考 `com.unity.ide.visualstudio` 的架构实现：

* 自动发现 Zed 安装，在 **Edit → Preferences → External Tools** 中作为可选代码编辑器出现；
* 双击报错/编译错误、`Assets` 面板中的脚本时，用 `zed "file:line:column"` 精确定位打开；
* 生成 `{项目名}.sln` 与每个程序集一个 `.csproj`，让 Zed 的 C# 语言服务器（OmniSharp / Roslyn）能够解析
  `UnityEngine`、`UnityEditor`、包内程序集，从而获得补全、跳转、诊断；
* 可选生成 Unity 友好的 `.zed/settings.json`（排除 `Library`、`Temp`、`obj`、`*.meta` 等），大幅降低大项目扫描开销；
* 兼容 `AssetPostprocessor.OnGeneratedCSProject` / `OnGeneratedSlnSolution` 等既有工程生成扩展点。

## 安装

把整个 `com.sgame.ide.zed` 目录放到项目的 `Packages/` 下（嵌入式包，无需修改 `manifest.json`），
或用 Package Manager 的 **Add package from disk…** 选择 `package.json`。

要求：Unity 2021.3+（编辑器平台专用程序集）。

## 使用

1. **Edit → Preferences → External Tools → External Script Editor** 选择 `Zed`；
   若没有出现，点击 *Browse…* 指向：
   * Windows：`%LOCALAPPDATA%\Programs\Zed\zed.exe`
   * macOS：`/Applications/Zed.app`（插件会自动解析到 `Contents/MacOS/cli`）
   * Linux：`/usr/bin/zed`、`/usr/local/bin/zed` 等；
2. 第一次选中 Zed 时会在项目根目录生成 `.sln` / `.csproj`，之后 Unity 增删脚本、修改 asmdef 会自动增量刷新；
3. 菜单 **Tools → Zed** 提供：打开工程、重新生成工程文件、写 `.zed/settings.json`、打开 External Tools 设置。

### Zed 侧准备

Zed 本身不含 C# 语言服务，需安装一个 C# 扩展（命令面板 → `zed: extensions`）：

* **OmniSharp**（`csharp` 扩展）：会使用工作区里的 `.sln`，Unity 场景下最常用；
* **Roslyn / dotnet** 相关扩展：同样通过 `.sln`/`.csproj` 解析。

打开 Unity 工程目录（`zed <项目根目录>`）后，语言服务器会以该目录为根启动，因此插件在冷启动时会
**先打开工程目录、再打开脚本**（可在 External Tools 面板关闭 `Open project folder first`）。

## 生成的内容

| 文件 | 说明 |
| --- | --- |
| `{ProjectName}.sln` | 经典 VS 解决方案，包含所有生成的程序集 |
| `Assembly-CSharp.csproj` 等 | SDK 风格工程，`TargetFramework=netstandard2.1`，显式 `Reference` Unity 程序集 |
| `*.Player.csproj` | 开启 *Player projects* 时额外生成 |
| `.zed/settings.json` | 仅在不存在时创建（面板中可覆盖） |

工程文件由 `UnityEditor.Compilation.CompilationPipeline` 的真实编译图生成，包含：
`DefineConstants`、`AllowUnsafeBlocks`、`LangVersion`、程序集引用、`ProjectReference`、
`csc.rsp` 中解析出的 `analyzer` / `additionalfile` / `langversion`、Unity 自带的 Roslyn 分析器。

> `.csproj` / `.sln` 是生成产物，请勿手改；需要定制时使用 `AssetPostprocessor.OnGeneratedCSProject`。

### 与其它生成器共存

Unity 6.6+ 内置的 MSBuild 生成、以及 Visual Studio / JetBrains 包也会写 `.csproj`（通常还有 `.slnx`）。
为避免互相覆盖，本插件**默认不覆盖已存在的、非本插件生成的 `.csproj`**，只补齐缺失的工程并始终写出
`.sln`（`.sln` 是 OmniSharp 唯一可靠识别的格式）。需要强制用本插件的格式重写时：

* 菜单 **Tools → Zed → Regenerate Project Files**；
* 或命令行 `ZedCommandLine.GenerateProjectFiles`。

## External Tools 面板选项

* **Generate .csproj files for**：按包来源（Embedded / Local / Registry / Git / Built-in / Local tarball / Unknown）
  决定是否把包内源码纳入工程；关闭时该包仅以 DLL 引用出现。
* **Player projects**：为每个运行时程序集额外生成 `*.Player.csproj`。
* **Create .zed/settings.json**：当文件不存在时自动写入；关闭后可用菜单手动写入。
* **Open project folder first**：冷启动时先打开工程目录，保证语言服务器根目录正确。

## 命令行

```bat
Unity.exe -batchmode -quit -projectPath "D:\MyGame" ^
  -executeMethod SGame.Zed.Editor.ZedCommandLine.GenerateProjectFiles
```

| 入口 | 行为 |
| --- | --- |
| `GenerateProjectFiles` | 强制重写 `.sln` 与全部 `.csproj` |
| `EnsureProjectFiles` | 只写 `.sln` 与缺失的 `.csproj`，保留其它工具生成的文件 |
| `WriteProjectSettings` | 内容不存在时写 `.zed/settings.json` |
| `OverwriteProjectSettings` | 强制重写 `.zed/settings.json` |

## 故障排查

| 现象 | 处理 |
| --- | --- |
| External Tools 里没有 Zed | 确认包已导入；点 *Browse…* 手动指定可执行文件；`ZED_EXECUTABLE_PATH` 环境变量可作为兜底 |
| 补全/跳转不生效 | 检查项目根目录是否生成了 `.sln`；在 Zed 中卸载并重载 C# 扩展；确认打开的是工程目录而不是单个脚本 |
| 打开脚本后语言服务根目录是子目录 | 保持 `Open project folder first` 开启，或先在 Zed 中打开工程目录 |
| 不想生成某些包 | 面板里取消对应来源，或把它们加入 `.zed/settings.json` 的排除项 |
| 与 Visual Studio / VS Code 包共存 | 三者不会同时运行生成器（Unity 只调用当前编辑器），切换编辑器后按需重新生成即可 |

## 已知限制

* 断点调试暂未实现：需要 Zed 的 DAP 支持搭配 `netcoredbg`/`vsdbg`，不在本包范围内；
* 生成的是经典 `.sln`（OmniSharp 只认 `.sln`）；Unity 6.6+ 还会生成 `.slnx`，两者可以并存；
* Unity 的编译产物仍是 `Library/ScriptAssemblies`，本插件只影响 IDE 侧工程文件。
