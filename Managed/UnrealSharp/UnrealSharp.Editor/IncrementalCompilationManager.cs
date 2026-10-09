using System.Collections.Immutable;
using System.Diagnostics;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using UnrealSharp.Editor.Interop;
using UnrealSharp.Editor.Utilities;
using UnrealSharp.Shared;
using UnrealSharp.UnrealSharpUtilities;

namespace UnrealSharp.Editor;

public static class IncrementalCompilationManager
{
    private static bool NeedsProjectStateRefresh(GenState? state)
    {
        return state == null
               || state.Driver == null
               || state.InitialCompilation == null
               || state.TreesByPath == null
               || state.Generators == null;
    }

    private static bool IsSkippablePath(string path)
    {
        string normalized = path.Replace('\\', '/');
        return normalized.Contains("/obj/", StringComparison.OrdinalIgnoreCase)
               || normalized.Contains("/bin/", StringComparison.OrdinalIgnoreCase);
    }

    // Matches native ECSCompileLogSeverity.
    private const int CompileLogSeverityWarning = 1;
    private const int CompileLogSeverityError = 2;

    // Parse diagnostics are kept per file until it parses again; project diagnostics until the project is regenerated.
    private const int CompileDiagnosticStageParse = 0;
    private const int CompileDiagnosticStageProject = 1;

    private static unsafe void ClearFileDiagnostics(string fullPath)
    {
        fixed (char* filePtr = fullPath)
        {
            Bind_FUnrealSharpEditorModule.CallClearFileCompileDiagnostics(filePtr);
        }
    }

    private static unsafe void BeginProjectDiagnostics(string projectName)
    {
        fixed (char* projectPtr = projectName)
        {
            Bind_FUnrealSharpEditorModule.CallBeginProjectCompileDiagnostics(projectPtr);
        }
    }

    private static void ReportDiagnostics(string projectName, IEnumerable<Diagnostic> diagnostics, int stage)
    {
        foreach (Diagnostic diagnostic in diagnostics)
        {
            if (diagnostic.IsSuppressed || diagnostic.Severity < DiagnosticSeverity.Warning)
            {
                continue;
            }

            FileLinePositionSpan span = diagnostic.Location.GetMappedLineSpan();
            bool hasLocation = span.IsValid && !string.IsNullOrEmpty(span.Path);
            string file = hasLocation ? span.Path : string.Empty;
            int line = hasLocation ? span.StartLinePosition.Line + 1 : 0;
            int column = hasLocation ? span.StartLinePosition.Character + 1 : 0;
            int severity = diagnostic.Severity == DiagnosticSeverity.Error ? CompileLogSeverityError : CompileLogSeverityWarning;
            string message = diagnostic.GetMessage();
            string fullText = diagnostic.ToString();

            unsafe
            {
                fixed (char* projectPtr = projectName)
                fixed (char* filePtr = file)
                fixed (char* codePtr = diagnostic.Id)
                fixed (char* messagePtr = message)
                fixed (char* fullTextPtr = fullText)
                {
                    Bind_FUnrealSharpEditorModule.CallReportCompileDiagnostic(projectPtr, filePtr, line, column, codePtr, severity, messagePtr, fullTextPtr, stage);
                }
            }
        }
    }

    public static void RemoveSourceFile(string projectName, string filepath)
    {
        Project? foundProject = SolutionManager.GetProjectByName(projectName);

        if (foundProject is null)
        {
            throw new Exception($"Project '{projectName}' not found in solution.");
        }

        GenState? state = SolutionManager.GetProjectState(foundProject.Id);
        if (state == null)
        {
            throw new Exception($"Project '{projectName}' not initialized for incremental generation.");
        }

        string fullPath = Path.GetFullPath(filepath);
        if (IsSkippablePath(fullPath))
        {
            return;
        }

        ClearFileDiagnostics(fullPath);

        if (!state.TreesByPath!.TryGetValue(fullPath, out SyntaxTree? existingTree))
        {
            return;
        }

        state.InitialCompilation = state.InitialCompilation!.RemoveSyntaxTrees(existingTree);
        state.TreesByPath.Remove(fullPath);
    }

    // Returns null on success, or the compiler errors when the file is rejected.
    // Expected compile failures are returned instead of thrown to avoid exception unwinding on every syntax error.
    public static string? RecompileChangedFile(string projectName, string filepath)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        Project? foundProject = SolutionManager.GetProjectByName(projectName);

        if (foundProject is null)
        {
            throw new Exception($"Project '{projectName}' not found in solution.");
        }

        GenState? state = SolutionManager.GetProjectState(foundProject.Id);
        if (state == null)
        {
            throw new Exception($"Project '{projectName}' not initialized for incremental generation.");
        }

        string fullPath = Path.GetFullPath(filepath);
        if (IsSkippablePath(fullPath))
        {
            return null;
        }

        string fileContent = File.ReadAllText(fullPath);
        SourceText newText = SourceText.From(fileContent, Encoding.UTF8);

        CSharpParseOptions parseOptions = (CSharpParseOptions)foundProject.ParseOptions!;
        SyntaxTree newTree = CSharpSyntaxTree.ParseText(newText, parseOptions, path: fullPath);

        if (newTree.GetDiagnostics().Any())
        {
            StringBuilder builder = new StringBuilder();

            foreach (Diagnostic diagnostic in newTree.GetDiagnostics())
            {
                if (diagnostic.Severity != DiagnosticSeverity.Error)
                {
                    continue;
                }

                builder.AppendLine(diagnostic.ToString());
            }

            if (builder.Length > 0)
            {
                // Parse warnings are reported later by Emit; only report here when the file is rejected.
                ClearFileDiagnostics(fullPath);
                ReportDiagnostics(projectName, newTree.GetDiagnostics(), CompileDiagnosticStageParse);
                return builder.ToString();
            }
        }

        ClearFileDiagnostics(fullPath);

        if (state.TreesByPath!.TryGetValue(fullPath, out SyntaxTree? existingTree))
        {
            state.InitialCompilation = state.InitialCompilation!.ReplaceSyntaxTree(existingTree, newTree);
        }
        else
        {
            state.InitialCompilation = state.InitialCompilation!.AddSyntaxTrees(newTree);
        }

        SyntaxUtilities.LookForChangesInUnrealTypes(newTree, existingTree, foundProject);

        state.TreesByPath[fullPath] = newTree;

        stopwatch.Stop();
        LogUnrealSharpEditor.Log($"Processed dirty file '{Path.GetFileName(filepath)}' in project '{projectName}' in {stopwatch.Elapsed.TotalMilliseconds:F2}ms.");
        return null;
    }

    // Returns null on success, or the compiler errors of the first project that failed to generate or emit.
    public static string? RecompileDirtyProjects(List<string> modifiedAssemblyNames)
    {
        List<Project> projects = ProjectUtilities.GetProjectsFromNames(modifiedAssemblyNames, SolutionManager.CurrentProjects);

        if (projects.Count != modifiedAssemblyNames.Count)
        {
            IEnumerable<string> missingAssemblyNames = modifiedAssemblyNames.Except(
                projects.Select(project => project.Name), StringComparer.Ordinal);
            throw new InvalidOperationException("Modified assemblies could not be resolved to Roslyn projects: "
                                                + string.Join(", ", missingAssemblyNames));
        }

        for (int i = projects.Count - 1; i >= 0; i--)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            Project project = projects[i];

            LogUnrealSharpEditor.Log($"Starting source generation for project '{project.Name}'.");

            GenState? state = SolutionManager.GetProjectState(project.Id);

            if (NeedsProjectStateRefresh(state))
            {
                LogUnrealSharpEditor.LogWarning(
                    $"Project '{project.Name}' incremental state is invalid. Rebuilding project state.");
                SolutionManager.ProcessProject(project).GetAwaiter().GetResult();
                state = SolutionManager.GetProjectState(project.Id);
            }

            if (state is null)
            {
                throw new Exception($"Project '{project.Name}' not initialized for incremental generation.");
            }

            CSharpParseOptions parseOptions = (CSharpParseOptions)project.ParseOptions!;
            AnalyzerConfigOptionsProvider analyzerOptions = project.AnalyzerOptions.AnalyzerConfigOptionsProvider;

            bool firstTime = state.Driver == null;
            bool analyzersChanged = state.AnalyzerRefCount != project.AnalyzerReferences.Count;
            bool additionalChanged = state.AdditionalDocCount != project.AdditionalDocuments.Count();
            bool optionsChanged = !ReferenceEquals(state.ParseOptions, parseOptions) ||
                                  !ReferenceEquals(state.AnalyzerOptions, analyzerOptions);
            bool refsChanged = state.MetadataRefCount != project.MetadataReferences.Count;

            if (firstTime || analyzersChanged)
            {
                state.AnalyzerRefCount = project.AnalyzerReferences.Count;
            }

            if (firstTime || state.AdditionalTexts.IsDefault || additionalChanged)
            {
                List<AdditionalText> additionalTexts = new List<AdditionalText>();
                foreach (TextDocument textDocument in project.AdditionalDocuments)
                {
                    Document document = (Document)textDocument;

                    if (document.FilePath is not null)
                    {
                        additionalTexts.Add(new FileAdditionalText(document.FilePath));
                    }
                }

                state.AdditionalTexts = ImmutableArray.CreateRange(additionalTexts);
                state.AdditionalDocCount = project.AdditionalDocuments.Count();
            }

            if (optionsChanged && state.Driver is not null)
            {
                state.Driver = state.Driver
                    .WithUpdatedParseOptions(parseOptions)
                    .WithUpdatedAnalyzerConfigOptions(analyzerOptions);
            }

            if (refsChanged)
            {
                state.MetadataRefCount = project.MetadataReferences.Count;
            }

            BeginProjectDiagnostics(project.Name);

            GeneratorDriver driver = state.Driver!.RunGeneratorsAndUpdateCompilation(
                state.InitialCompilation!,
                out Compilation updatedCompilation,
                out ImmutableArray<Diagnostic> genDiagnosticsInner);

            state.Driver = driver;

            if (genDiagnosticsInner.Any())
            {
                ReportDiagnostics(project.Name, genDiagnosticsInner, CompileDiagnosticStageProject);
                StringBuilder stringBuilder = new StringBuilder();
                foreach (Diagnostic diagnostic in genDiagnosticsInner)
                {
                    if (diagnostic.Severity != DiagnosticSeverity.Error)
                    {
                        continue;
                    }

                    stringBuilder.AppendLine(diagnostic.ToString());
                }

                if (stringBuilder.Length > 0)
                {
                    return "Source generator failed:\n" + stringBuilder;
                }
            }

            state.ParseOptions = parseOptions;
            state.AnalyzerOptions = analyzerOptions;

            UpdateDependentProjectsWithNewCompilation(updatedCompilation, project);

            stopwatch.Stop();
            LogUnrealSharpEditor.Log($"Project '{project.Name}' generated source in {stopwatch.Elapsed.TotalSeconds:F2} seconds.");
            string? emitError = EmitResultsToDisk(project, updatedCompilation);
            if (emitError != null)
            {
                return emitError;
            }
        }

        List<string> assemblies = new List<string>(SolutionManager.UnrealSharpWorkspace.CurrentSolution.Projects.Count());

        foreach (Project project in SolutionManager.UnrealSharpWorkspace.CurrentSolution.Projects)
        {
            assemblies.Add(GetAssemblyOutputPath(project));
        }

        string outputDir = Path.GetDirectoryName(assemblies[0])!;
        
        LoadOrderOptions loadOrderOptions = new LoadOrderOptions() { Collectible = true, Priority = 0 };
        AssemblyUtilities.EmitLoadOrder(assemblies, outputDir, loadOrderOptions, "UserCode");
        return null;
    }

    private static void UpdateDependentProjectsWithNewCompilation(Compilation newCompilation, Project producedProject)
    {
        IEnumerable<Project> dependentProjects = producedProject.GetDependentProjects(SolutionManager.CurrentProjects);

        foreach (Project dependentProject in dependentProjects)
        {
            GenState? projectState = SolutionManager.GetProjectState(dependentProject.Id);

            if (projectState is null)
            {
                throw new Exception($"Project '{dependentProject.Name}' not initialized for incremental generation.");
            }

            if (projectState.InitialCompilation is null)
            {
                throw new Exception($"Project '{dependentProject.Name}' has no initial compilation.");
            }

            Compilation depCompilation = projectState.InitialCompilation;

            MetadataReference? oldCompilationReference = null;
            foreach (MetadataReference reference in depCompilation.References)
            {
                if (reference is not CompilationReference compilationReference)
                {
                    continue;
                }

                if (compilationReference.Compilation.AssemblyName != newCompilation.AssemblyName)
                {
                    continue;
                }

                oldCompilationReference = reference;
                break;
            }

            CompilationReference newCompilationReference = newCompilation.ToMetadataReference();

            if (oldCompilationReference != null)
            {
                depCompilation = depCompilation.ReplaceReference(oldCompilationReference, newCompilationReference);
            }
            else
            {
                depCompilation = depCompilation.AddReferences(newCompilationReference);
            }

            projectState.InitialCompilation = depCompilation;
        }
    }

    private static string GetOutputPath(Project project, string extension)
    {
        string userAssemblyDir = UCSPathsBlueprintFunctionLibrary.UserAssemblyDirectory;

        if (!Directory.Exists(userAssemblyDir))
        {
            Directory.CreateDirectory(userAssemblyDir);
        }

        return Path.Combine(userAssemblyDir, project.AssemblyName + extension);
    }

    private static string GetAssemblyOutputPath(Project project)
    {
        string extension = project.OutputFilePath != null ? Path.GetExtension(project.OutputFilePath) : ".dll";
        return GetOutputPath(project, extension);
    }

    private static string GetDebugSymbolExtension()
    {
        if (OperatingSystem.IsWindows())
        {
            return ".pdb";
        }

        if (OperatingSystem.IsMacOS())
        {
            return ".dSYM";
        }

        return ".so.debug";
    }

    // Returns null on success, or the emit errors.
    private static string? EmitResultsToDisk(Project project, Compilation updatedCompilation)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        string assemblyPath = GetAssemblyOutputPath(project);
        string symbolsPath = GetOutputPath(project, GetDebugSymbolExtension());
        string assemblyTempPath = assemblyPath + ".tmp";
        string symbolsTempPath = symbolsPath + ".tmp";

        EmitOptions emitOptions = new EmitOptions(debugInformationFormat: DebugInformationFormat.PortablePdb);
        EmitResult emitResult;

        using (FileStream assemblyStream = File.Create(assemblyTempPath))
        using (FileStream symbolsStream = File.Create(symbolsTempPath))
        {
            emitResult = updatedCompilation.Emit(assemblyStream, symbolsStream, options: emitOptions);
        }

        ReportDiagnostics(project.Name, emitResult.Diagnostics, CompileDiagnosticStageProject);

        if (!emitResult.Success)
        {
            StringBuilder stringBuilder = new StringBuilder(256);

            foreach (Diagnostic diagnostic in emitResult.Diagnostics)
            {
                if (diagnostic.Severity != DiagnosticSeverity.Error)
                {
                    continue;
                }

                if (stringBuilder.Length > 0)
                {
                    stringBuilder.Append(Environment.NewLine);
                }

                stringBuilder.Append(diagnostic);
            }

            if (File.Exists(assemblyTempPath))
            {
                File.Delete(assemblyTempPath);
            }

            if (File.Exists(symbolsTempPath))
            {
                File.Delete(symbolsTempPath);
            }

            return stringBuilder.ToString();
        }

        File.Move(assemblyTempPath, assemblyPath, true);
        File.Move(symbolsTempPath, symbolsPath, true);

        stopwatch.Stop();
        LogUnrealSharpEditor.Log($"Project '{project.Name}' produced an assembly in {stopwatch.Elapsed.TotalSeconds:F2} seconds.");
        return null;
    }
}
