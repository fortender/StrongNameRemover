using Mono.Cecil;
using System.Reflection;

namespace StrongNameRemover
{
    internal class Program
    {
        static int Main(string[] args)
        {
            if (args is not [string sourcePath, string destinationPath])
            {
                Console.Error.WriteLine("Usage: <src dir> <dst dir>");
                return 2;
            }

            if (!Directory.Exists(destinationPath))
                Directory.CreateDirectory(destinationPath);

            try
            {

                // Create dependency graph over assemblies in given source path
                List<ModuleWrapper> modules = CreateGraph(sourcePath);

                // Perform strong name removal for assembly files having patched prefix
                foreach (ModuleWrapper module in modules.Where(m => m.Module.FileName.Contains(".Patched")))
                    RemoveStrongNameReferences(module);

                // Write changed modules to given destination path
                foreach (ModuleWrapper module in modules)
                {
                    try
                    {
                        string fileName = module.Module.FileName;

                        if (module.HasChanges)
                        {
                            module.Module.Write(Path.Combine(destinationPath, Path.GetFileName(fileName)));
                            Console.WriteLine("Creating patch for {0}", fileName);
                        }
                        else
                        {
                            Console.WriteLine("No changes in {0}", fileName);
                        }
                    }
                    finally
                    {
                        module.Module.Dispose();
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Error: {0}", ex);
                return 1;
            }
        }

        static void RemoveStrongNameReferences(ModuleWrapper module)
        {
            var set = new HashSet<ModuleWrapper>();
            var queue = new Queue<ModuleWrapper>([module]);

            while (queue.TryDequeue(out var element))
            {
                if (element.HasChanges || !set.Add(element))
                    continue;

                // Remove strong name of current module
                RemoveStrongNameFromModule(element.Module);
                element.HasChanges = true;

                Console.WriteLine("Removed strong name of file: {0}", element.Module.FileName);

                foreach (var referenced in element.ReferencedBy)
                {
                    // Remove strong name from reference
                    AssemblyNameReference assemblyNameReference = referenced.Value.Module.AssemblyReferences.First(anr => anr.Name == element.Module.Assembly.Name.Name);
                    RemoveStrongNameFromAssemblyReference(assemblyNameReference);
                    // referenced.Value.HasChanges = true;

                    Console.WriteLine("Removed strong name reference {0} -> {1}", referenced.Key, element.Module.FileName);

                    queue.Enqueue(referenced.Value);
                }

                foreach (var referenced in element.ReferencedByInternalsVisibleTo)
                {
                    var attribute = referenced.Value.Module.Assembly.CustomAttributes
                        .Where(a => a.AttributeType.Name == "InternalsVisibleToAttribute")
                        .Single(a => new AssemblyName((string)a.ConstructorArguments.First().Value).Name!.Equals(element.Module.Assembly.Name.Name, StringComparison.OrdinalIgnoreCase));

                    var asmName = new AssemblyName((string)attribute.ConstructorArguments.First().Value);

                    if (asmName.GetPublicKey() is not { Length: > 0 })
                    {
                        Console.WriteLine("{0} [{1}]: Skipping InternalsVisibleToAttribute({2}) - no strong name", referenced.Key, referenced.Value.Module.Assembly.Name.Name, asmName.Name);
                    }

                    // Replace strong name ref with simple name ref
                    var ctorArgType = attribute.ConstructorArguments.First().Type;
                    attribute.ConstructorArguments.RemoveAt(0);
                    attribute.ConstructorArguments.Insert(0, new CustomAttributeArgument(ctorArgType, asmName.Name));

                    Console.WriteLine("{0} [{1}]: Removing strong name from InternalsVisibleToAttribute({2})", referenced.Key, referenced.Value.Module.Assembly.Name.Name, asmName.FullName);

                    queue.Enqueue(referenced.Value);
                }
            }
        }

        static List<ModuleWrapper> CreateGraph(string sourcePath)
        {
            // First pass: Load all assemblies in folder (top-level only)
            var lookup = Directory.EnumerateFiles(sourcePath)
                .Select(LoadSafe)
                .Where(m => m != null)
                .ToLookup(m => m!.Assembly.Name.Name, m => new ModuleWrapper(m!));

            // Second pass: Add references
            foreach (ModuleWrapper module in lookup.SelectMany(g => g))
            {
                foreach (var asmNameRef in module.Module.AssemblyReferences)
                {
                    foreach (var moduleRef in lookup[asmNameRef.Name])
                    {
                        module.References.Add(moduleRef.Module.FileName, moduleRef);
                        moduleRef.ReferencedBy.Add(module.Module.FileName, module);
                    }
                }

                foreach (var attribute in module.Module.Assembly.CustomAttributes)
                {
                    if (attribute.AttributeType.Name == "InternalsVisibleToAttribute")
                    {
                        CustomAttributeArgument ctorArg = attribute.ConstructorArguments.First();
                        var asmName = new AssemblyName((string)ctorArg.Value);
                        foreach (var moduleRef in lookup[asmName.Name!])
                        {
                            moduleRef.ReferencedByInternalsVisibleTo.Add(module.Module.FileName, module);
                        }
                    }
                }
            }

            return [.. lookup.SelectMany(g => g)];
        }

        static ModuleDefinition? LoadSafe(string fileName)
        {
            try
            {
                return ModuleDefinition.ReadModule(fileName);
            }
            catch
            {
                return null;
            }
        }

        static void RemoveStrongNameFromModule(ModuleDefinition modDef)
        {
            modDef.Attributes &= ~ModuleAttributes.StrongNameSigned;

            var asmName = modDef.Assembly.Name;
            asmName.HasPublicKey = false;
            asmName.PublicKey = [];
            asmName.PublicKeyToken = null;
        }

        static void RemoveStrongNameFromAssemblyReference(AssemblyNameReference assemblyNameReference)
        {
            assemblyNameReference.HasPublicKey = false;
            assemblyNameReference.PublicKey = [];
            assemblyNameReference.PublicKeyToken = null;
        }

    }

    public class ModuleWrapper(ModuleDefinition module)
    {
        public ModuleDefinition Module { get; } = module;
        public Dictionary<string, ModuleWrapper> References { get; } = [];
        public Dictionary<string, ModuleWrapper> ReferencedBy { get; } = [];
        public Dictionary<string, ModuleWrapper> ReferencedByInternalsVisibleTo { get; } = [];
        public bool HasChanges { get; set; }
    }

}
