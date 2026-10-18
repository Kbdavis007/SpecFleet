using System.Xml.Linq;

namespace SpecFleet.Test.Architecture;

public sealed class ArchitectureTests
{
    private static readonly IReadOnlyDictionary<string, string[]> ExpectedReferences =
        new Dictionary<string, string[]>
        {
            ["src/SpecFleet.Service/SpecFleet.Service.csproj"] = [],
            ["src/SpecFleet.Api/SpecFleet.Api.csproj"] =
            [
                "src/SpecFleet.Service/SpecFleet.Service.csproj"
            ],
            ["src/SpecFleet.Workers/SpecFleet.Workers.csproj"] =
            [
                "src/SpecFleet.Service/SpecFleet.Service.csproj"
            ],
            ["tests/SpecFleet.Test/SpecFleet.Test.csproj"] =
            [
                "src/SpecFleet.Api/SpecFleet.Api.csproj",
                "src/SpecFleet.Service/SpecFleet.Service.csproj",
                "src/SpecFleet.Workers/SpecFleet.Workers.csproj"
            ]
        };

    [Fact]
    public void Repository_contains_exactly_the_five_expected_projects()
    {
        var root = FindRepositoryRoot();
        var projectFiles = Directory
            .EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !HasBuildOutputSegment(root, path))
            .Select(path => ToRepositoryPath(root, path))
            .Order(StringComparer.Ordinal)
            .ToArray();

        Assert.Equal(ExpectedReferences.Keys.Order(StringComparer.Ordinal), projectFiles);

        var uiManifest = Path.Combine(root, "src", "SpecFleet.Ui", "package.json");
        Assert.True(File.Exists(uiManifest), "The independently deployed Ui project is missing.");

        var clientManifests = Directory
            .EnumerateFiles(root, "package.json", SearchOption.AllDirectories)
            .Where(path => !HasBuildOutputSegment(root, path))
            .Select(path => ToRepositoryPath(root, path))
            .ToArray();

        Assert.Equal(["src/SpecFleet.Ui/package.json"], clientManifests);
    }

    [Fact]
    public void Project_references_follow_the_service_boundary()
    {
        var root = FindRepositoryRoot();

        foreach (var (projectPath, expectedReferences) in ExpectedReferences)
        {
            var projectFile = Path.Combine(root, projectPath);
            var projectDirectory = Path.GetDirectoryName(projectFile)!;
            var document = XDocument.Load(projectFile);
            var references = document
                .Descendants("ProjectReference")
                .Select(element =>
                {
                    var include = element.Attribute("Include")!.Value
                        .Replace('\\', Path.DirectorySeparatorChar)
                        .Replace('/', Path.DirectorySeparatorChar);
                    return Path.GetFullPath(Path.Combine(projectDirectory, include));
                })
                .Select(path => ToRepositoryPath(root, path))
                .Order(StringComparer.Ordinal)
                .ToArray();

            Assert.Equal(expectedReferences.Order(StringComparer.Ordinal), references);
        }
    }

    [Fact]
    public void Source_files_follow_vertical_slice_and_thin_host_layouts()
    {
        var root = FindRepositoryRoot();
        var serviceRoot = Path.Combine(root, "src", "SpecFleet.Service");
        var apiRoot = Path.Combine(root, "src", "SpecFleet.Api");
        var workersRoot = Path.Combine(root, "src", "SpecFleet.Workers");

        AssertNoGlobalLayerFolders(serviceRoot);
        AssertSourceFilesUnder(serviceRoot, ["Features", "Shared"]);
        AssertSourceFilesUnder(apiRoot, ["Features"], allowedRootFiles: ["Program.cs"]);
        AssertSourceFilesUnder(workersRoot, ["Consumers"], allowedRootFiles: ["Program.cs"]);

        var apiProgram = File.ReadAllText(Path.Combine(apiRoot, "Program.cs"));
        Assert.DoesNotContain(".MapGet(", apiProgram, StringComparison.Ordinal);
        Assert.DoesNotContain(".MapPost(", apiProgram, StringComparison.Ordinal);
        Assert.DoesNotContain(".MapPut(", apiProgram, StringComparison.Ordinal);
        Assert.DoesNotContain(".MapDelete(", apiProgram, StringComparison.Ordinal);
    }

    [Fact]
    public void Architecture_rule_rejects_a_global_repository_layer()
    {
        var root = Directory.CreateTempSubdirectory("SpecFleet.Architecture.");

        try
        {
            Directory.CreateDirectory(Path.Combine(root.FullName, "Repositories"));

            var violations = FindGlobalLayerFolders(root.FullName);

            Assert.Contains("Repositories", violations);
        }
        finally
        {
            root.Delete(recursive: true);
        }
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "SpecFleet.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate SpecFleet.sln from the test output directory.");
    }

    private static void AssertNoGlobalLayerFolders(string serviceRoot)
    {
        var violations = FindGlobalLayerFolders(serviceRoot);
        Assert.Empty(violations);
    }

    private static string[] FindGlobalLayerFolders(string root)
    {
        string[] forbiddenNames = ["Controllers", "Services", "Repositories", "Managers"];
        return Directory
            .EnumerateDirectories(root, "*", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => name is not null && forbiddenNames.Contains(name, StringComparer.Ordinal))
            .Select(name => name!)
            .ToArray();
    }

    private static void AssertSourceFilesUnder(
        string projectRoot,
        string[] allowedDirectories,
        string[]? allowedRootFiles = null)
    {
        var sourceFiles = Directory
            .EnumerateFiles(projectRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !HasBuildOutputSegment(projectRoot, path));

        foreach (var sourceFile in sourceFiles)
        {
            var relativePath = Path.GetRelativePath(projectRoot, sourceFile);
            var segments = relativePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var isAllowedRootFile = segments.Length == 1
                && allowedRootFiles?.Contains(segments[0], StringComparer.Ordinal) == true;
            var isAllowedFeatureFile = allowedDirectories.Contains(segments[0], StringComparer.Ordinal);

            Assert.True(
                isAllowedRootFile || isAllowedFeatureFile,
                $"{relativePath} is outside the allowed vertical-slice/consumer folders for {projectRoot}.");
        }
    }

    private static bool HasBuildOutputSegment(string root, string path)
    {
        var relativePath = Path.GetRelativePath(root, path);
        return relativePath
            .Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            .Any(segment => segment is "bin" or "obj" or "node_modules" or "dist");
    }

    private static string ToRepositoryPath(string root, string path) =>
        Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/');
}
