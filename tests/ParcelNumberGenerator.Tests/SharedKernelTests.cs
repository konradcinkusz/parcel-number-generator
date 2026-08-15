using System.Reflection;
using ParcelNumberGenerator.Data;
using ParcelNumberGenerator.Domain;
using ParcelNumberGenerator.ServiceDefaults;

namespace ParcelNumberGenerator.Tests;

/// <summary>
/// The shared kernel's boundary, asserted rather than described.
/// </summary>
/// <remarks>
/// P2 makes this mechanical on purpose: the estate has twice watched a "shared plumbing"
/// library grow into a shared domain — entities, pricing, seed data — and in both cases the
/// rule against it was written down and did not hold. A prose limit is a suggestion; a
/// failing test is not.
/// </remarks>
public sealed class SharedKernelTests
{
    private static readonly Assembly Kernel = typeof(Extensions).Assembly;

    [Fact]
    public void The_kernel_does_not_reference_the_domain_or_the_data_layer()
    {
        string[] forbidden =
        [
            typeof(NumberPool).Assembly.GetName().Name!,
            typeof(UsedNumber).Assembly.GetName().Name!,
        ];

        string[] referenced = [.. Kernel.GetReferencedAssemblies().Select(name => name.Name!)];

        Assert.Empty(referenced.Intersect(forbidden, StringComparer.Ordinal));
    }

    [Fact]
    public void The_kernel_exports_only_extension_methods_and_the_types_they_need()
    {
        // The other half of P2: no base classes to derive from. Every capability is an
        // extension method over IHostApplicationBuilder, IServiceCollection or
        // WebApplication, so a service opts in line by line instead of inheriting a frame.
        Type[] inheritable =
        [
            .. Kernel.GetExportedTypes()
                .Where(type => type is { IsClass: true, IsAbstract: true, IsSealed: false }),
        ];

        Assert.Empty(inheritable);
    }

    [Fact]
    public void The_kernel_stays_under_its_size_ceiling()
    {
        // P2's ceiling is ~800 lines, checked mechanically. The number is not sacred; being
        // forced to justify crossing it is the point, because domain drift arrives as a
        // series of individually reasonable additions.
        const int ceiling = 800;

        string kernelDirectory = Path.Combine(RepositoryRoot(), "src", "ParcelNumberGenerator.ServiceDefaults");
        int lines = Directory
            .EnumerateFiles(kernelDirectory, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Sum(path => File.ReadAllLines(path).Length);

        Assert.True(lines <= ceiling, $"The shared kernel is {lines} lines, over the {ceiling}-line ceiling.");
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Directory.Build.props")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return directory.FullName;
    }
}
