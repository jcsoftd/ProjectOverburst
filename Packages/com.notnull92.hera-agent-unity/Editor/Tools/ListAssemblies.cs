using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;

namespace HeraAgent.Tools
{
    [HeraTool(
        Name = "list_assemblies",
        Description = "List assemblies loaded in the current Unity Editor AppDomain. AI agents call this to find which Unity packages, project assemblies, and third-party libraries are available before writing exec code.",
        Profiles = new[] { "diagnostics" },
        RiskClass = HeraRiskClass.ReadOnly,
        ContractMode = ToolContractMode.Strict,
        Examples = new[]
        {
            "list_assemblies",
            "list_assemblies --filter Unity.Entities",
            "list_assemblies --include_version true",
            "list_assemblies --include_location true",
        },
        ExampleDescriptions = new[]
        {
            "List project + Unity assembly names (system DLLs filtered out; bare name strings)",
            "Filter by substring (case-insensitive)",
            "Return {name, version} objects instead of bare names",
            "Include DLL file paths (implies version; default off — most AI agents don't need them)",
        })]
    public static class ListAssemblies
    {
        [Newtonsoft.Json.JsonObject(NamingStrategyType = typeof(Newtonsoft.Json.Serialization.SnakeCaseNamingStrategy))]
        public sealed class Result
        {
            public int Count { get; set; }
            public string[] Assemblies { get; set; }
        }


        public class Parameters
        {
            [ToolParameter("Case-insensitive substring filter on assembly name.")]
            public string Filter { get; set; }

            [ToolParameter("Include System.*, mscorlib, netstandard etc. Default false because AI agents rarely need them.")]
            public bool IncludeSystem { get; set; }

            [ToolParameter("Return {name, version} objects instead of bare name strings. Default false — most assemblies report 0.0.0.0, so the version is dropped to roughly halve the payload.")]
            public bool IncludeVersion { get; set; }

            [ToolParameter("Include each assembly's DLL location (full path), implies version. Default false — most AI agents don't need them.")]
            public bool IncludeLocation { get; set; }
        }

        private static readonly string[] SystemPrefixes =
        {
            "System.", "mscorlib", "netstandard", "Microsoft.", "Mono.",
            "WindowsBase", "PresentationCore", "PresentationFramework",
        };

        public static object HandleCommand(JObject @params)
        {
            var p = new ToolParams(@params);
            var filter = p.Get("filter");
            var includeSystem = p.GetBool("include_system");
            var includeVersion = p.GetBool("include_version");
            var includeLocation = p.GetBool("include_location");

            var sortedAssemblies = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => !a.IsDynamic)
                .Select(TryDescribeAssembly)
                .Where(a => a != null)
                .OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase);

            var result = new List<object>();
            foreach (var descriptor in sortedAssemblies)
            {
                var asm = descriptor.Assembly;
                var name = descriptor.Name;

                if (!includeSystem && SystemPrefixes.Any(prefix =>
                    name.StartsWith(prefix, StringComparison.Ordinal) || string.Equals(name, prefix.TrimEnd('.'), StringComparison.Ordinal)))
                    continue;

                if (!string.IsNullOrEmpty(filter) &&
                    name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                if (includeLocation)
                {
                    string location = null;
                    try { location = asm.Location; } catch { /* dynamic / in-memory */ }
                    result.Add(new { name, version = descriptor.Version, location });
                }
                else if (includeVersion)
                {
                    result.Add(new { name, version = descriptor.Version });
                }
                else
                {
                    // Bare name — most assemblies report 0.0.0.0, so the version
                    // field is noise the agent rarely reads. Opt in with
                    // --include_version when it actually matters.
                    result.Add(name);
                }
            }

            return new SuccessResponse($"{result.Count} assemblies", new
            {
                count = result.Count,
                assemblies = result,
            });
        }

        private static AssemblyDescriptor TryDescribeAssembly(Assembly assembly)
        {
            try
            {
                // RuntimeAssembly.GetName() also resolves CodeBase on Mono. A
                // non-UTF8 native path can make that conversion throw before
                // the assembly name is returned, so parse FullName first and
                // skip only the malformed entry if even that is unavailable.
                var identity = new AssemblyName(assembly.FullName);
                if (string.IsNullOrEmpty(identity.Name)) return null;
                return new AssemblyDescriptor
                {
                    Assembly = assembly,
                    Name = identity.Name,
                    Version = identity.Version?.ToString(),
                };
            }
            catch
            {
                return null;
            }
        }

        private sealed class AssemblyDescriptor
        {
            public Assembly Assembly { get; set; }
            public string Name { get; set; }
            public string Version { get; set; }
        }
    }
}
