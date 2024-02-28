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
    /// executes them on the methods they're targetting in the given modules.
    /// </summary>
    /// <param name="modules"></param>
    public static void PatchAll(params ModuleDefinition[] modules)
    {

        // Get all of the annotated methods
        var patches =
            typeof(PatchHelpers).Assembly
            .GetExportedTypes()
            .SelectMany(t => t.GetMethods())
            .Select(m => (attr: m.GetCustomAttribute<MethodPatchAttribute>()!, method: m))
            .Where(a => a.attr != null);


        Program.Msg("Got patches");

        // Loop over modules
        foreach (ModuleDefinition module in modules)
        {   
            // Check if any modules have already been processed
            bool weaved =
                module
                .CustomAttributes
                .Any(a => 
                    a.ConstructorArguments.Any(a =>
                        a.Value as string == PROCESSED_TAG
                    )
                );
            
            // If they have, skip them so we don't double process any
            if (weaved)
            {
                Program.Msg($"{module.Name} already weaved, skipping!");
                continue;
            }

            // Loop over each patch method
            foreach (var (attr, method) in patches)
            {

                // Get potential matches
                var targets =
                    module.Types
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
            }
            
            
            // Construct a new description attribute to mark the assemblies as already-processed so they aren't accidentally patched multiple times on subsequent runs of this patcher
            CustomAttribute tag = new(module.ImportReference(typeof(DescriptionAttribute).GetConstructor([ typeof(string) ])));
            
            // Create a new string argument with the PROCESSED_TAG and add it to the description attribute
            tag.ConstructorArguments.Add(new(module.ImportReference(typeof(string)), PROCESSED_TAG));

            // Add the attribute to the module to mark it
            module.CustomAttributes.Add(tag);

            // Write the module back to a file.
            module.Write();
            
        }
    }



    /// <summary>
    /// Patches a <see cref="MethodDefinition"/> by invoking a patch method
    /// </summary>
    /// <param name="definition">Definition to patch</param>
    /// <param name="patch">The <see cref="MethodInfo"/> to invoke for patching the definition</param>
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

}
