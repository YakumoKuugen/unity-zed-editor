using System;
using System.Collections.Generic;
using System.IO;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Asm = UnityEditor.Compilation.Assembly;

namespace SGame.Zed.Editor.ProjectGeneration
{
    /// <summary>
    /// Generates a Visual Studio solution (<c>.sln</c>) plus one SDK style <c>.csproj</c> per assembly.
    /// Zed itself does not read these files, but every C# language server available to Zed
    /// (OmniSharp, Roslyn) resolves Unity assemblies through them.
    /// </summary>
    internal sealed class ZedProjectGenerator
    {
        internal const string k_NewLine = "\r\n";

        // C# class library project type, the only project type we generate.
        private const string k_ProjectTypeGuid = "FAE04EC0-301F-11D3-BF4B-00C04F79EFBC";

        private readonly string m_ProjectDirectory;
        private readonly string m_ProjectName;
        private readonly HashSet<string> m_SupportedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly PackageInfoCache m_PackageCache;
        private readonly Dictionary<string, AssemblyEntry> m_Entries = new Dictionary<string, AssemblyEntry>(StringComparer.OrdinalIgnoreCase);

        // Reused by incremental syncs: rebuilding it costs an AssetDatabase.GetAllAssetPaths() call.
        private Dictionary<string, string> m_AssetParts;

        public ZedProjectGenerator()
            : this(GetDefaultProjectDirectory())
        {
        }

        public ZedProjectGenerator(string projectDirectory)
        {
            m_ProjectDirectory = PathUtility.TrimTrailingSeparator(PathUtility.GetAbsolutePath(projectDirectory));
            m_ProjectName = Path.GetFileName(m_ProjectDirectory);
            m_PackageCache = new PackageInfoCache(m_ProjectDirectory);

            RefreshSupportedExtensions();
        }

        public string ProjectDirectory => m_ProjectDirectory;

        public static string GetDefaultProjectDirectory()
        {
            return PathUtility.GetAbsolutePath(Path.GetDirectoryName(Application.dataPath) ?? ".");
        }

        public string SolutionFile()
        {
            return Path.Combine(m_ProjectDirectory, SanitizeFileName(m_ProjectName) + ".sln");
        }

        public bool HasSolutionBeenGenerated()
        {
            return File.Exists(SolutionFile());
        }

        /// <summary>
        /// Regenerates the solution and the projects. Only files whose content changed are written.
        /// When <paramref name="force"/> is <c>false</c>, existing <c>.csproj</c> files produced by
        /// another generator (Unity's built-in MSBuild pipeline, the Visual Studio package, ...) are
        /// left untouched: the generated solution simply references them.
        /// </summary>
        public void Sync(bool force = false)
        {
            m_PackageCache.Clear();
            RefreshSupportedExtensions();

            try
            {
                if (GenerationCallbacks.OnPreGeneratingCSProjectFiles())
                    return;

                var entries = GetAssemblies();
                RebuildEntryLookup(entries);

                var assetParts = GenerateAssetProjectParts();
                m_AssetParts = assetParts;

                WriteFileIfChanged(SolutionFile(), SolutionText(entries));

                foreach (var entry in entries)
                {
                    if (!force && ShouldPreserveExistingProject(ProjectFile(entry)))
                        continue;

                    WriteProject(entry, assetParts);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Zed] Project generation failed: {exception}");
            }
            finally
            {
                GenerationCallbacks.OnGeneratedCSProjectFiles();
            }
        }

        /// <summary>
        /// Regenerates only what is impacted by <paramref name="affectedFiles"/> /
        /// <paramref name="reimportedFiles"/>. Returns <c>false</c> when nothing relevant changed.
        /// </summary>
        public bool SyncIfNeeded(IEnumerable<string> affectedFiles, IEnumerable<string> reimportedFiles)
        {
            var touchedProjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var needsFullSync = false;
            var needsAssets = false;

            RefreshSupportedExtensions();

            if (affectedFiles != null)
            {
                foreach (var file in affectedFiles)
                {
                    if (string.IsNullOrEmpty(file) || !IsSupportedFile(file))
                        continue;

                    var extension = GetExtensionWithoutDot(file);
                    if (extension == "asmdef" || extension == "dll")
                    {
                        // Assembly definitions and plugins can change the whole compilation graph.
                        needsFullSync = true;
                        continue;
                    }

                    if (extension != "cs")
                        needsAssets = true;

                    var assemblyName = GetAssemblyNameFromScriptPath(file);
                    if (string.IsNullOrEmpty(assemblyName))
                    {
                        needsFullSync = true;
                        continue;
                    }

                    touchedProjects.Add(Path.GetFileNameWithoutExtension(assemblyName));
                }
            }

            if (reimportedFiles != null)
            {
                foreach (var file in reimportedFiles)
                {
                    var extension = GetExtensionWithoutDot(file);
                    if (extension == "asmdef" || extension == "dll")
                        needsFullSync = true;
                }
            }

            if (needsFullSync || needsAssets)
            {
                Sync();
                return true;
            }

            if (touchedProjects.Count == 0)
                return false;

            try
            {
                m_PackageCache.Clear();

                var entries = GetAssemblies();
                RebuildEntryLookup(entries);

                // Asset items are part of every project file, so they must never be dropped.
                var assetParts = m_AssetParts ?? (m_AssetParts = GenerateAssetProjectParts());

                WriteFileIfChanged(SolutionFile(), SolutionText(entries));

                var updated = 0;
                foreach (var entry in entries)
                {
                    if (!touchedProjects.Contains(entry.Assembly.name) && !touchedProjects.Contains(entry.ProjectName))
                        continue;

                    if (ShouldPreserveExistingProject(ProjectFile(entry)))
                        continue;

                    WriteProject(entry, assetParts);
                    updated++;
                }

                return updated > 0;
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Zed] Incremental project generation failed, falling back to a full sync: {exception}");
                Sync();
                return true;
            }
        }

        public bool IsSupportedFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            return m_SupportedExtensions.Contains(GetExtensionWithoutDot(path));
        }

        private void RebuildEntryLookup(List<AssemblyEntry> entries)
        {
            m_Entries.Clear();
            foreach (var entry in entries)
                m_Entries[entry.ProjectName] = entry;
        }

        private void RefreshSupportedExtensions()
        {
            m_SupportedExtensions.Clear();
            m_SupportedExtensions.Add("cs");
            m_SupportedExtensions.Add("dll");
            m_SupportedExtensions.Add("asmdef");
            m_SupportedExtensions.Add("additionalfile");

            AddExtensions(EditorSettings.projectGenerationUserExtensions);
            AddExtensions(EditorSettings.projectGenerationBuiltinExtensions);
        }

        private void AddExtensions(string[] extensions)
        {
            if (extensions == null)
                return;

            foreach (var extension in extensions)
            {
                if (string.IsNullOrEmpty(extension))
                    continue;

                m_SupportedExtensions.Add(extension.TrimStart('.').ToLowerInvariant());
            }
        }

        private static string GetExtensionWithoutDot(string path)
        {
            if (string.IsNullOrEmpty(path))
                return string.Empty;

            var extension = Path.GetExtension(path);
            if (string.IsNullOrEmpty(extension))
                return string.Empty;

            return extension.Substring(1).ToLowerInvariant();
        }

        private bool IsInternalizedPackagePath(string path)
        {
            var packageInfo = m_PackageCache.FindForPath(path);
            if (packageInfo == null)
                return false;

            var flags = ZedPreferences.GenerationFlags;
            switch (packageInfo.source)
            {
                case UnityEditor.PackageManager.PackageSource.Embedded:
                    return (flags & ProjectGenerationFlag.Embedded) == 0;
                case UnityEditor.PackageManager.PackageSource.Local:
                    return (flags & ProjectGenerationFlag.Local) == 0;
                case UnityEditor.PackageManager.PackageSource.LocalTarball:
                    return (flags & ProjectGenerationFlag.LocalTarBall) == 0;
                case UnityEditor.PackageManager.PackageSource.Registry:
                    return (flags & ProjectGenerationFlag.Registry) == 0;
                case UnityEditor.PackageManager.PackageSource.BuiltIn:
                    return (flags & ProjectGenerationFlag.BuiltIn) == 0;
                case UnityEditor.PackageManager.PackageSource.Git:
                    return (flags & ProjectGenerationFlag.Git) == 0;
                default:
                    return (flags & ProjectGenerationFlag.Unknown) == 0;
            }
        }

        private bool ShouldIncludeFile(string file)
        {
            if (string.IsNullOrEmpty(file) || !IsSupportedFile(file))
                return false;

            return !IsInternalizedPackagePath(file);
        }

        private List<AssemblyEntry> GetAssemblies()
        {
            var entries = new List<AssemblyEntry>();

            AddAssemblies(entries, AssembliesType.Editor, isPlayer: false);

            if (ZedPreferences.HasFlag(ProjectGenerationFlag.PlayerAssemblies))
                AddAssemblies(entries, AssembliesType.Player, isPlayer: true);

            return entries;
        }

        private void AddAssemblies(List<AssemblyEntry> entries, AssembliesType type, bool isPlayer)
        {
            Asm[] assemblies;
            try
            {
                assemblies = CompilationPipeline.GetAssemblies(type);
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Zed] Could not enumerate {type} assemblies: {exception.Message}");
                return;
            }

            foreach (var assembly in assemblies)
            {
                if (assembly == null || assembly.sourceFiles == null || assembly.sourceFiles.Length == 0)
                    continue;

                var relevant = false;
                foreach (var sourceFile in assembly.sourceFiles)
                {
                    if (ShouldIncludeFile(sourceFile))
                    {
                        relevant = true;
                        break;
                    }
                }

                if (relevant)
                    entries.Add(new AssemblyEntry(assembly, isPlayer));
            }
        }

        private static string GetAssemblyNameFromScriptPath(string path)
        {
            try
            {
                return CompilationPipeline.GetAssemblyNameFromScriptPath(path);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Collects the non-script assets (shaders, uxml, asmdef, ...) grouped by owning assembly.</summary>
        private Dictionary<string, string> GenerateAssetProjectParts()
        {
            var builders = new Dictionary<string, StringBuilder>(StringComparer.OrdinalIgnoreCase);

            string[] assetPaths;
            try
            {
                assetPaths = AssetDatabase.GetAllAssetPaths();
            }
            catch (Exception exception)
            {
                Debug.LogWarning($"[Zed] Could not enumerate assets: {exception.Message}");
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            foreach (var assetPath in assetPaths)
            {
                if (string.IsNullOrEmpty(assetPath))
                    continue;

                if (!IsSupportedFile(assetPath))
                    continue;

                // Scripts are compiled by the assembly they belong to.
                if (GetExtensionWithoutDot(assetPath) == "cs")
                    continue;

                if (IsInternalizedPackagePath(assetPath))
                    continue;

                var assemblyName = GetAssemblyNameFromScriptPath(assetPath);
                if (string.IsNullOrEmpty(assemblyName))
                    continue;

                var key = Path.GetFileNameWithoutExtension(assemblyName);
                if (string.IsNullOrEmpty(key))
                    continue;

                if (!builders.TryGetValue(key, out var builder))
                {
                    builder = new StringBuilder();
                    builders[key] = builder;
                }

                AppendInclude(builder, "None", assetPath);
            }

            var result = new Dictionary<string, string>(builders.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var pair in builders)
                result[pair.Key] = pair.Value.ToString();

            return result;
        }

        private string ProjectFile(AssemblyEntry entry)
        {
            return Path.Combine(m_ProjectDirectory, entry.ProjectName + ".csproj");
        }

        private void WriteProject(AssemblyEntry entry, Dictionary<string, string> assetParts)
        {
            var path = ProjectFile(entry);
            var content = GenerationCallbacks.OnGeneratedCSProject(path, ProjectText(entry, assetParts));
            WriteFileIfChanged(path, content);
        }

        /// <summary>
        /// Unity 6.6+ (and the Visual Studio / JetBrains packages) already generate <c>.csproj</c>
        /// files. Overwriting them on every sync would fight over the same file, so foreign files are
        /// preserved unless the user explicitly asks for a regeneration.
        /// </summary>
        private static bool ShouldPreserveExistingProject(string path)
        {
            if (!File.Exists(path))
                return false;

            try
            {
                using (var reader = new StreamReader(path))
                {
                    var buffer = new char[1024];
                    var read = reader.Read(buffer, 0, buffer.Length);
                    return new string(buffer, 0, read).IndexOf("com.sgame.ide.zed", StringComparison.Ordinal) < 0;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private string ProjectText(AssemblyEntry entry, Dictionary<string, string> assetParts)
        {
            var assembly = entry.Assembly;
            var options = assembly.compilerOptions;
            var responseFiles = GetResponseFileData(assembly);

            var defines = CollectDefines(assembly, responseFiles);
            var langVersion = GetLangVersion(assembly, responseFiles);
            var nullable = GetNullable(responseFiles);
            var allowUnsafe = IsUnsafeAllowed(assembly, responseFiles);
            var rootNamespace = assembly.rootNamespace ?? string.Empty;

            var builder = new StringBuilder(8192);

            builder.Append("<Project>").Append(k_NewLine);
            builder.Append("  <!-- Generated by com.sgame.ide.zed, do not edit. Use an AssetPostprocessor.OnGeneratedCSProject hook to customize. -->").Append(k_NewLine);

            // Must be set before Sdk.props is imported so MSBuild uses it for the intermediate folder.
            builder.Append(@"  <PropertyGroup>").Append(k_NewLine);
            builder.Append(@"    <BaseIntermediateOutputPath>Temp\obj\$(MSBuildProjectName)</BaseIntermediateOutputPath>").Append(k_NewLine);
            builder.Append(@"    <IntermediateOutputPath>$(BaseIntermediateOutputPath)</IntermediateOutputPath>").Append(k_NewLine);
            builder.Append(@"    <AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath>").Append(k_NewLine);
            builder.Append(@"    <UseCommonOutputDirectory>true</UseCommonOutputDirectory>").Append(k_NewLine);
            builder.Append(@"    <OutputPath>").Append(entry.RelativeOutputPath).Append(@"</OutputPath>").Append(k_NewLine);
            builder.Append(@"  </PropertyGroup>").Append(k_NewLine);

            builder.Append(@"  <Import Project=""Sdk.props"" Sdk=""Microsoft.NET.Sdk"" />").Append(k_NewLine);

            builder.Append(@"  <ItemGroup>").Append(k_NewLine);
            builder.Append(@"    <ProjectCapability Include=""Unity"" />").Append(k_NewLine);
            builder.Append(@"  </ItemGroup>").Append(k_NewLine);

            builder.Append(@"  <PropertyGroup>").Append(k_NewLine);
            builder.Append(@"    <GenerateAssemblyInfo>false</GenerateAssemblyInfo>").Append(k_NewLine);
            builder.Append(@"    <EnableDefaultItems>false</EnableDefaultItems>").Append(k_NewLine);
            builder.Append(@"    <LangVersion>").Append(XmlEscape(langVersion)).Append(@"</LangVersion>").Append(k_NewLine);
            if (!string.IsNullOrEmpty(nullable))
                builder.Append(@"    <Nullable>").Append(XmlEscape(nullable)).Append(@"</Nullable>").Append(k_NewLine);
            builder.Append(@"    <RootNamespace>").Append(XmlEscape(rootNamespace)).Append(@"</RootNamespace>").Append(k_NewLine);
            builder.Append(@"    <OutputType>Library</OutputType>").Append(k_NewLine);
            builder.Append(@"    <AssemblyName>").Append(XmlEscape(assembly.name)).Append(@"</AssemblyName>").Append(k_NewLine);
            builder.Append(@"    <TargetFramework>netstandard2.1</TargetFramework>").Append(k_NewLine);
            builder.Append(@"    <BaseDirectory>.</BaseDirectory>").Append(k_NewLine);
            builder.Append(@"  </PropertyGroup>").Append(k_NewLine);

            builder.Append(@"  <PropertyGroup>").Append(k_NewLine);
            builder.Append(@"    <NoWarn>0169;USG0001</NoWarn>").Append(k_NewLine);
            builder.Append(@"    <DefineConstants>").Append(XmlEscape(string.Join(";", defines))).Append(@"</DefineConstants>").Append(k_NewLine);
            builder.Append(@"    <AllowUnsafeBlocks>").Append(allowUnsafe ? "true" : "false").Append(@"</AllowUnsafeBlocks>").Append(k_NewLine);
            builder.Append(@"  </PropertyGroup>").Append(k_NewLine);

            // Unity provides every reference explicitly, so the implicit framework references must go away.
            builder.Append(@"  <PropertyGroup>").Append(k_NewLine);
            builder.Append(@"    <NoStandardLibraries>true</NoStandardLibraries>").Append(k_NewLine);
            builder.Append(@"    <NoStdLib>true</NoStdLib>").Append(k_NewLine);
            builder.Append(@"    <NoConfig>true</NoConfig>").Append(k_NewLine);
            builder.Append(@"    <DisableImplicitFrameworkReferences>true</DisableImplicitFrameworkReferences>").Append(k_NewLine);
            builder.Append(@"    <MSBuildWarningsAsMessages>MSB3277</MSBuildWarningsAsMessages>").Append(k_NewLine);
            builder.Append(@"  </PropertyGroup>").Append(k_NewLine);

            builder.Append(@"  <PropertyGroup>").Append(k_NewLine);
            builder.Append(@"    <UnityProjectGenerator>SGame.Zed.Editor</UnityProjectGenerator>").Append(k_NewLine);
            builder.Append(@"    <UnityVersion>").Append(XmlEscape(Application.unityVersion)).Append(@"</UnityVersion>").Append(k_NewLine);
            builder.Append(@"    <UnityBuildTarget>").Append(EditorUserBuildSettings.activeBuildTarget).Append(@"</UnityBuildTarget>").Append(k_NewLine);
            builder.Append(@"  </PropertyGroup>").Append(k_NewLine);

            AppendAnalyzerAndAdditionalFiles(builder, options, responseFiles);

            AppendSourceFiles(builder, entry, out var fileReferences);

            if (!entry.IsPlayer && assetParts != null && assetParts.TryGetValue(assembly.name, out var additionalAssets) && !string.IsNullOrEmpty(additionalAssets))
            {
                builder.Append(@"  <ItemGroup>").Append(k_NewLine);
                builder.Append(additionalAssets);
                builder.Append(@"  </ItemGroup>").Append(k_NewLine);
            }

            AppendReferences(builder, entry, responseFiles, fileReferences);
            AppendProjectReferences(builder, entry);

            builder.Append(@"  <Import Project=""Sdk.targets"" Sdk=""Microsoft.NET.Sdk"" />").Append(k_NewLine);
            builder.Append("</Project>").Append(k_NewLine);

            return builder.ToString();
        }

        private void AppendSourceFiles(StringBuilder builder, AssemblyEntry entry, out List<string> dllReferences)
        {
            dllReferences = new List<string>();

            builder.Append(@"  <ItemGroup>").Append(k_NewLine);

            var sourceFiles = entry.Assembly.sourceFiles;
            if (sourceFiles != null)
            {
                foreach (var file in sourceFiles)
                {
                    if (string.IsNullOrEmpty(file) || !ShouldIncludeFile(file))
                        continue;

                    if (GetExtensionWithoutDot(file) == "dll")
                    {
                        dllReferences.Add(file);
                        continue;
                    }

                    AppendInclude(builder, "Compile", file);
                }
            }

            builder.Append(@"  </ItemGroup>").Append(k_NewLine);
        }

        private void AppendReferences(StringBuilder builder, AssemblyEntry entry, ResponseFileData[] responseFiles, List<string> fileReferences)
        {
            builder.Append(@"  <ItemGroup>").Append(k_NewLine);

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var reference in EnumerateReferencePaths(entry, responseFiles, fileReferences))
            {
                if (string.IsNullOrEmpty(reference))
                    continue;

                var name = Path.GetFileNameWithoutExtension(reference);
                if (string.IsNullOrEmpty(name) || !seen.Add(name))
                    continue;

                var fullPath = Path.IsPathRooted(reference)
                    ? reference
                    : Path.Combine(m_ProjectDirectory, reference);

                builder.Append(@"    <Reference Include=""").Append(XmlEscape(name)).Append(@""">").Append(k_NewLine);
                builder.Append(@"      <HintPath>").Append(XmlEscape(PathUtility.NormalizeSeparators(fullPath))).Append(@"</HintPath>").Append(k_NewLine);
                builder.Append(@"      <Private>False</Private>").Append(k_NewLine);
                builder.Append(@"    </Reference>").Append(k_NewLine);
            }

            builder.Append(@"  </ItemGroup>").Append(k_NewLine);
        }

        private IEnumerable<string> EnumerateReferencePaths(AssemblyEntry entry, ResponseFileData[] responseFiles, List<string> fileReferences)
        {
            var assembly = entry.Assembly;

            if (assembly.compiledAssemblyReferences != null)
            {
                foreach (var reference in assembly.compiledAssemblyReferences)
                    yield return reference;
            }

            foreach (var responseFile in responseFiles)
            {
                foreach (var reference in responseFile.FullPathReferences)
                    yield return reference;
            }

            if (fileReferences != null)
            {
                foreach (var reference in fileReferences)
                    yield return reference;
            }

            if (assembly.assemblyReferences != null)
            {
                foreach (var referencedAssembly in assembly.assemblyReferences)
                {
                    if (referencedAssembly == null)
                        continue;

                    // Assemblies that get their own project are referenced as ProjectReference instead.
                    if (TryResolveProjectName(entry, referencedAssembly, out _))
                        continue;

                    yield return referencedAssembly.outputPath;
                }
            }
        }

        private void AppendProjectReferences(StringBuilder builder, AssemblyEntry entry)
        {
            var references = entry.Assembly.assemblyReferences;
            if (references == null || references.Length == 0)
                return;

            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var group = new StringBuilder();

            foreach (var referencedAssembly in references)
            {
                if (referencedAssembly == null)
                    continue;

                if (!TryResolveProjectName(entry, referencedAssembly, out var projectName))
                    continue;

                if (string.Equals(projectName, entry.ProjectName, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!seen.Add(projectName))
                    continue;

                group.Append(@"    <ProjectReference Include=""").Append(XmlEscape(projectName)).Append(@".csproj"" />").Append(k_NewLine);
            }

            if (group.Length == 0)
                return;

            builder.Append(@"  <ItemGroup>").Append(k_NewLine);
            builder.Append(group);
            builder.Append(@"  </ItemGroup>").Append(k_NewLine);
        }

        private bool TryResolveProjectName(AssemblyEntry owner, Asm reference, out string projectName)
        {
            projectName = null;

            if (owner.IsPlayer)
            {
                var playerName = reference.name + ".Player";
                if (m_Entries.TryGetValue(playerName, out var playerEntry))
                {
                    projectName = playerEntry.ProjectName;
                    return true;
                }
            }

            if (m_Entries.TryGetValue(reference.name, out var entry) && !entry.IsPlayer)
            {
                projectName = entry.ProjectName;
                return true;
            }

            return false;
        }

        private void AppendAnalyzerAndAdditionalFiles(StringBuilder builder, ScriptCompilerOptions options, ResponseFileData[] responseFiles)
        {
            var analyzers = new List<string>();
            var additionalFiles = new List<string>();
            var rulesetPath = string.Empty;

            if (options != null)
            {
                if (options.RoslynAnalyzerDllPaths != null)
                    analyzers.AddRange(options.RoslynAnalyzerDllPaths);

                rulesetPath = options.RoslynAnalyzerRulesetPath;
            }

            foreach (var responseFile in responseFiles)
            {
                analyzers.AddRange(GetOtherArguments(responseFile, "analyzer"));
                analyzers.AddRange(GetOtherArguments(responseFile, "a"));
                additionalFiles.AddRange(GetOtherArguments(responseFile, "additionalfile"));
            }

            if (!string.IsNullOrEmpty(rulesetPath))
            {
                builder.Append(@"  <PropertyGroup>").Append(k_NewLine);
                builder.Append(@"    <CodeAnalysisRuleSet>").Append(XmlEscape(NormalizePath(rulesetPath))).Append(@"</CodeAnalysisRuleSet>").Append(k_NewLine);
                builder.Append(@"  </PropertyGroup>").Append(k_NewLine);
            }

            AppendItemGroup(builder, "Analyzer", analyzers);

            if (additionalFiles.Count > 0)
                AppendItemGroup(builder, "AdditionalFiles", additionalFiles);
        }

        private void AppendItemGroup(StringBuilder builder, string tag, List<string> values)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var group = new StringBuilder();

            foreach (var value in values)
            {
                if (string.IsNullOrEmpty(value))
                    continue;

                var path = NormalizePath(value);
                if (!seen.Add(path))
                    continue;

                group.Append(@"    <").Append(tag).Append(@" Include=""").Append(XmlEscape(path)).Append(@""" />").Append(k_NewLine);
            }

            if (group.Length == 0)
                return;

            builder.Append(@"  <ItemGroup>").Append(k_NewLine);
            builder.Append(group);
            builder.Append(@"  </ItemGroup>").Append(k_NewLine);
        }

        private string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            var fullPath = Path.IsPathRooted(path) ? path : Path.Combine(m_ProjectDirectory, path);
            return PathUtility.NormalizeSeparators(fullPath);
        }

        private void AppendInclude(StringBuilder builder, string tag, string asset)
        {
            var includePath = ResolveIncludePath(asset, out var linkPath);

            builder.Append(@"    <").Append(tag).Append(@" Include=""").Append(includePath).Append('"');

            if (!string.IsNullOrEmpty(linkPath))
            {
                builder.Append(">").Append(k_NewLine);
                builder.Append(@"      <Link>").Append(XmlEscape(linkPath)).Append(@"</Link>").Append(k_NewLine);
                builder.Append(@"    </").Append(tag).Append(">").Append(k_NewLine);
            }
            else
            {
                builder.Append(" />").Append(k_NewLine);
            }
        }

        private string ResolveIncludePath(string asset, out string linkPath)
        {
            linkPath = null;

            var path = PathUtility.NormalizeSeparators(asset);

            // Package assets are reported through their virtual path; map them back to disk.
            if (!Path.IsPathRooted(path) && path.StartsWith("Packages" + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                var physical = m_PackageCache.ResolvePhysicalPath(path);
                if (!string.IsNullOrEmpty(physical))
                    path = PathUtility.NormalizeSeparators(physical);
            }

            if (PathUtility.IsSubPathOf(m_ProjectDirectory, path))
                return XmlEscape(PathUtility.SkipPathPrefix(path, m_ProjectDirectory));

            linkPath = m_PackageCache.GetLinkPath(path);
            return XmlEscape(path);
        }

        private ResponseFileData[] GetResponseFileData(Asm assembly)
        {
            var options = assembly.compilerOptions;
            if (options == null || options.ResponseFiles == null || options.ResponseFiles.Length == 0)
                return Array.Empty<ResponseFileData>();

            string[] systemReferenceDirectories;
            try
            {
                systemReferenceDirectories = CompilationPipeline.GetSystemAssemblyDirectories(options.ApiCompatibilityLevel);
            }
            catch (Exception)
            {
                systemReferenceDirectories = Array.Empty<string>();
            }

            var result = new List<ResponseFileData>(options.ResponseFiles.Length);
            foreach (var responseFile in options.ResponseFiles)
            {
                if (string.IsNullOrEmpty(responseFile))
                    continue;

                try
                {
                    result.Add(CompilationPipeline.ParseResponseFile(responseFile, m_ProjectDirectory, systemReferenceDirectories));
                }
                catch (Exception exception)
                {
                    Debug.LogWarning($"[Zed] Could not parse response file '{responseFile}': {exception.Message}");
                }
            }

            return result.ToArray();
        }

        private static List<string> CollectDefines(Asm assembly, ResponseFileData[] responseFiles)
        {
            var defines = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            void Add(string define)
            {
                if (string.IsNullOrEmpty(define))
                    return;

                define = define.Trim();
                if (define.Length == 0 || !seen.Add(define))
                    return;

                defines.Add(define);
            }

            if (assembly.defines != null)
            {
                foreach (var define in assembly.defines)
                    Add(define);
            }

            foreach (var responseFile in responseFiles)
            {
                foreach (var define in responseFile.Defines)
                    Add(define);
            }

            return defines;
        }

        private static string GetLangVersion(Asm assembly, ResponseFileData[] responseFiles)
        {
            foreach (var responseFile in responseFiles)
            {
                foreach (var value in GetOtherArguments(responseFile, "langversion"))
                {
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }
            }

            var options = assembly.compilerOptions;
            if (options != null && !string.IsNullOrEmpty(options.LanguageVersion))
                return options.LanguageVersion;

            return "9.0";
        }

        private static string GetNullable(ResponseFileData[] responseFiles)
        {
            foreach (var responseFile in responseFiles)
            {
                foreach (var value in GetOtherArguments(responseFile, "nullable"))
                {
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }
            }

            return string.Empty;
        }

        private static bool IsUnsafeAllowed(Asm assembly, ResponseFileData[] responseFiles)
        {
            var options = assembly.compilerOptions;
            if (options != null && options.AllowUnsafeCode)
                return true;

            foreach (var responseFile in responseFiles)
            {
                if (responseFile.OtherArguments == null)
                    continue;

                foreach (var argument in responseFile.OtherArguments)
                {
                    if (string.IsNullOrEmpty(argument))
                        continue;

                    var trimmed = argument.Trim();
                    if (trimmed == "/unsafe" || trimmed == "-unsafe")
                        return true;
                }
            }

            return false;
        }

        private static IEnumerable<string> GetOtherArguments(ResponseFileData responseFile, string name)
        {
            if (responseFile.OtherArguments == null)
                yield break;

            foreach (var argument in responseFile.OtherArguments)
            {
                if (string.IsNullOrEmpty(argument))
                    continue;

                var trimmed = argument.Trim();
                if (trimmed.Length < 3 || (trimmed[0] != '/' && trimmed[0] != '-'))
                    continue;

                var separator = trimmed.IndexOf(':');
                if (separator <= 1)
                    continue;

                if (!string.Equals(trimmed.Substring(1, separator - 1).Trim(), name, StringComparison.OrdinalIgnoreCase))
                    continue;

                var value = trimmed.Substring(separator + 1).Trim().Trim('"');
                if (value.Length > 0)
                    yield return value;
            }
        }

        private string SolutionText(List<AssemblyEntry> entries)
        {
            var builder = new StringBuilder(1024 + entries.Count * 200);

            builder.Append(k_NewLine);
            builder.Append("Microsoft Visual Studio Solution File, Format Version 12.00").Append(k_NewLine);
            builder.Append("# Visual Studio 15").Append(k_NewLine);
            builder.Append("VisualStudioVersion = 15.0.28307.1000").Append(k_NewLine);
            builder.Append("MinimumVisualStudioVersion = 10.0.40219.1").Append(k_NewLine);

            foreach (var entry in entries)
            {
                builder.AppendFormat(@"Project(""{{{0}}}"") = ""{1}"", ""{1}.csproj"", ""{{{2}}}""", k_ProjectTypeGuid, entry.ProjectName, ProjectGuid(entry)).Append(k_NewLine);
                builder.Append("EndProject").Append(k_NewLine);
            }

            builder.Append("Global").Append(k_NewLine);
            builder.Append("\tGlobalSection(SolutionConfigurationPlatforms) = preSolution").Append(k_NewLine);
            builder.Append("\t\tDebug|Any CPU = Debug|Any CPU").Append(k_NewLine);
            builder.Append("\t\tRelease|Any CPU = Release|Any CPU").Append(k_NewLine);
            builder.Append("\tEndGlobalSection").Append(k_NewLine);
            builder.Append("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution").Append(k_NewLine);

            foreach (var entry in entries)
            {
                var guid = ProjectGuid(entry);
                builder.Append($"\t\t{{{guid}}}.Debug|Any CPU.ActiveCfg = Debug|Any CPU").Append(k_NewLine);
                builder.Append($"\t\t{{{guid}}}.Debug|Any CPU.Build.0 = Debug|Any CPU").Append(k_NewLine);
                builder.Append($"\t\t{{{guid}}}.Release|Any CPU.ActiveCfg = Release|Any CPU").Append(k_NewLine);
                builder.Append($"\t\t{{{guid}}}.Release|Any CPU.Build.0 = Release|Any CPU").Append(k_NewLine);
            }

            builder.Append("\tEndGlobalSection").Append(k_NewLine);
            builder.Append("\tGlobalSection(SolutionProperties) = preSolution").Append(k_NewLine);
            builder.Append("\t\tHideSolutionNode = FALSE").Append(k_NewLine);
            builder.Append("\tEndGlobalSection").Append(k_NewLine);
            builder.Append("EndGlobal").Append(k_NewLine);

            return GenerationCallbacks.OnGeneratedSlnSolution(SolutionFile(), builder.ToString());
        }

        private string ProjectGuid(AssemblyEntry entry)
        {
            return ComputeGuid(m_ProjectName + "|" + entry.ProjectName + "|zed");
        }

        private static string ComputeGuid(string input)
        {
            using (var md5 = MD5.Create())
            {
                var hash = md5.ComputeHash(Encoding.UTF8.GetBytes(input));
                var builder = new StringBuilder(36);
                for (var i = 0; i < 16; i++)
                {
                    if (i == 4 || i == 6 || i == 8 || i == 10)
                        builder.Append('-');

                    builder.Append(hash[i].ToString("X2"));
                }

                return builder.ToString();
            }
        }

        private static string SanitizeFileName(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "Assembly";

            var builder = new StringBuilder(name.Length);
            var invalid = Path.GetInvalidFileNameChars();
            foreach (var character in name)
            {
                var isInvalid = false;
                foreach (var invalidCharacter in invalid)
                {
                    if (character == invalidCharacter)
                    {
                        isInvalid = true;
                        break;
                    }
                }

                builder.Append(isInvalid ? '_' : character);
            }

            return builder.ToString();
        }

        private static string XmlEscape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;

            return SecurityElement.Escape(value);
        }

        private void WriteFileIfChanged(string path, string content)
        {
            try
            {
                if (File.Exists(path))
                {
                    var existing = File.ReadAllText(path);
                    if (string.Equals(existing, content, StringComparison.Ordinal))
                        return;
                }
                else
                {
                    var directory = Path.GetDirectoryName(path);
                    if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                        Directory.CreateDirectory(directory);
                }

                File.WriteAllText(path, content, new UTF8Encoding(false));
            }
            catch (Exception exception)
            {
                Debug.LogError($"[Zed] Could not write '{path}': {exception.Message}");
            }
        }
    }
}
