using System.ComponentModel;
using System.Reflection;
using Mono.Cecil;
using Mono.Cecil.Cil;
using Mono.Cecil.Rocks;
using MethodBody = Mono.Cecil.Cil.MethodBody;


namespace Cumulonimbus;

/// <summary>
/// Helper class for patching assemblies
/// </summary>
public static class PatchHelpers
{
    /// <summary>
    /// Assembly marker to avoid multiple weavings of the same assembly
    /// </summary>
    public const string PROCESSED_TAG = "CUMULO_PROCESSED";



    /// <summary>
    /// Finds all methods annotated with <see cref="MethodPatchAttribute"/> and
    /// executes them on the methods they're targetting in the given module.
    /// </summary>
    /// <param name="module">The module to patch</param>
    public static int PatchAll(ModuleDefinition module)
    {

        // Get all of the annotated methods
        var patches =
            typeof(PatchHelpers).Assembly
            .GetExportedTypes()
            .SelectMany(t => t.GetMethods())
            .Select(m => (attr: m.GetCustomAttribute<MethodPatchAttribute>()!, method: m))
            .Where(a => a.attr != null);


        Program.Msg("Got patches");


#if DEBUG
        List<string> moduleTypes = module.GetAllNestedTypes().Select(t => t.FullName).ToList();
        moduleTypes.Sort();
        foreach (var type in moduleTypes)
        {
            Program.Debug($"Module has type: {type}");
        }
#endif


        // Check if the module has already been processed
        bool weaved =
            module
            .CustomAttributes
            .Any(a => 
                a.ConstructorArguments.Any(a =>
                    a.Value as string == PROCESSED_TAG
                )
            );
        
        // If it has, skip them so we don't double process any
        if (weaved)
        {
            Program.Msg($"{module.Name} already weaved, skipping!");
            return 0;
        }

        // Loop over each patch method
        int numPatched = 0;
        foreach (var (attr, method) in patches)
        {

            // Get potential matches
            var targets =
                module.GetAllNestedTypes()
                .FirstOrDefault(t => t.FullName == attr.TypeName)?
                .GetMethods()
                .Where(m => m.Name == attr.MethodName);

            // Narrow it done to one match based on whether the parameters were matched. If no parameters were defined, just get the first match instead.
            MethodDefinition? patchTarget =
                targets?.FirstOrDefault(m =>
                {
                    // Program.Msg($"Checking method {m.FullName}");
                    return attr.Signature.Length == 0 || m.Parameters.Select(p => p.ParameterType.Name).SequenceEqual(attr.Signature);
                });
            
            // Invoke the patch method on the patch target
            patchTarget?.PatchMethod(method);
            
            if (patchTarget != null)
                numPatched++;
        }
        

        // Only mark the assembly if at least one patch target was found
        if (numPatched > 0)
        {
            // Construct a new description attribute to mark the assemblies as already-processed so they aren't accidentally patched multiple times on subsequent runs of this patcher
            CustomAttribute tag = new(module.ImportReference(typeof(DescriptionAttribute).GetConstructor([ typeof(string) ])));
            
            // Create a new string argument with the PROCESSED_TAG and add it to the description attribute
            tag.ConstructorArguments.Add(new(module.ImportReference(typeof(string)), PROCESSED_TAG));

            // Add the attribute to the module to mark it
            module.CustomAttributes.Add(tag);
        }
        return numPatched;
    }




#pragma warning disable CS0419 // Ambiguous reference in cref attribute
    /// <summary>
    /// Patches a <see cref="MethodDefinition"/> by invoking a patch method
    /// </summary>
    /// <param name="definition">Definition to patch</param>
    /// <param name="patch">The <see cref="MethodInfo"/> to invoke for patching the definition</param>
#pragma warning restore CS0419 // Ambiguous reference in cref attribute
    private static void PatchMethod(this MethodDefinition definition, MethodInfo patch)
    {
        Program.Msg($"Patching {definition.FullName} with {patch.Name}!\n");

        // Get the method body
        MethodBody body = definition.Body;

        // Get the processor for that body
        ILProcessor processor = body.GetILProcessor();

        // The patch is invoked and its instructions are stored in a list. This is much like a Harmony transpiler, but far less fun.
        List<Instruction> newInstructions = (patch.Invoke(null, [body.Instructions, definition.Module, processor]) as IEnumerable<Instruction>)!.ToList();

        // Clear the processor of all the old OpCodes.
        processor.Clear();

        // Add the new, modified instruction set back into the method definition.
        newInstructions.ForEach(processor.Append);
    }



    /// <summary>
    /// Returns the MethodInfo of any given delegate or function reference
    /// </summary>
    /// <param name="d"></param>
    /// <returns></returns>
    public static MethodInfo GetMethodInfo(Delegate d)
    {
        return d.Method;
    }



    /// <summary>
    /// Copied from https://learn.microsoft.com/en-us/dotnet/standard/io/how-to-copy-directories
    /// <para>&#x0a;</para>
    /// Why.
    /// </summary>
    /// <param name="sourceDir">The source directory to copy from</param>
    /// <param name="destinationDir">The destination to copy the directory to</param>
    /// <param name="recursive">Makes the copy recursive</param>
    /// <param name="overWrite">Overwrites existing files</param>
    /// <exception cref="DirectoryNotFoundException"></exception>
    public static void CopyDirectory(string sourceDir, string destinationDir, bool recursive = false, bool overWrite = false)
    {
        // Get information about the source directory
        var dir = new DirectoryInfo(sourceDir);

        // Check if the source directory exists
        if (!dir.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {dir.FullName}");

        // Cache directories before we start copying
        DirectoryInfo[] dirs = dir.GetDirectories();

        // Create the destination directory
        Directory.CreateDirectory(destinationDir);

        // Get the files in the source directory and copy to the destination directory
        foreach (FileInfo file in dir.GetFiles())
        {
            Program.Msg($"Copying {file.Name} to {Path.Combine(destinationDir, file.Name)}");
            string targetFilePath = Path.Combine(destinationDir, file.Name);
            file.CopyTo(targetFilePath, overWrite);
        }

        // If recursive and copying subdirectories, recursively call this method
        if (recursive)
        {
            foreach (DirectoryInfo subDir in dirs)
            {
                string newDestinationDir = Path.Combine(destinationDir, subDir.Name);
                CopyDirectory(subDir.FullName, newDestinationDir, true, overWrite);
            }
        }
    }



    /// <summary>
    /// Searches a module for all nested types recursively
    /// </summary>
    /// <param name="module">The module to query</param>
    /// <returns></returns>
    public static IEnumerable<TypeDefinition> GetAllNestedTypes(this ModuleDefinition module)
    {
        return module.Types.SelectMany(t => t.GetNestedTypeDefinitions());
    }



    /// <summary>
    /// Gets all nested definitions from a type definition recursively
    /// </summary>
    /// <param name="typeDef">The type to query</param>
    /// <returns></returns>
    public static IEnumerable<TypeDefinition> GetNestedTypeDefinitions(this TypeDefinition typeDef)
    {
        yield return typeDef;

        foreach (TypeDefinition type in typeDef.NestedTypes.SelectMany(t => t.GetNestedTypeDefinitions()))
        {
            yield return type;
        }
    }
}
