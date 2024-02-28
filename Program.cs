using System.Net;
using Mono.Cecil;
using Mono.Cecil.Cil;
using System.CommandLine;
using MethodAttributes = Mono.Cecil.MethodAttributes;
using ParameterAttributes = Mono.Cecil.ParameterAttributes;

namespace Cumulonimbus;

#pragma warning disable CS1591

public class Program
{
    public static readonly string[] targetModules = ["FrooxEngine.dll", "FrooxEngine.Weaver.dll"];
    public static readonly ReaderParameters readParams = new();

#pragma warning restore CS1591

    /// <summary>
    /// Main entry point, uses System.CommandLine for CLI
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns></returns>
    public static async Task<int> Main(string[] args)
    {
        RootCommand root = new("Patches the Resonite headless to be compatible with the .NET 8 Runtime");

        Argument<string> path = new(
            "path",
            "The path of the Resonite Headless directory");
        
        Option<bool> noConfirm = new(
            "--noconfirm",
            () => false,
            "Runs Cumulo without asking for user confirmation (useful in headless startup scripts)");

        root.AddArgument(path);
        root.AddOption(noConfirm);
        root.SetHandler((path, noConfirm) => {            
            if (!Directory.Exists(path))
            {
                Console.WriteLine("Cannot find directory, check the path for errors and try again");
                return;
            }

            Execute(path, noConfirm);

        }, path, noConfirm);

        return await root.InvokeAsync(args);
    }



    /// <summary>
    /// Executes the patcher and re-weaves the targetted assemblies
    /// </summary>
    /// <param name="path">The path of the Resonite Headless folder</param>
    /// <param name="noConfirm">Whether to run without user input</param>
    public static void Execute(string path, bool noConfirm)
    {   
        Msg("Starting Cumulo patcher!");

        if (!noConfirm)
        {
            string prompt = string.Join(Environment.NewLine,
            "",
            "--------",
            $"You are about to apply Cumulo patches to \"{path}\".",
            "This operation is NOT reversible and will make your",
            "headless server incompatible with Mono/.NET Framework",
            "in order to support .NET 8.",
            "--------",
            "",
            "Do you want to continue? (y/N): ");

            Console.Write(prompt);
            
            var confirmation = Console.ReadLine();

            Console.WriteLine();

            if (confirmation?.ToLower(System.Globalization.CultureInfo.CurrentCulture) is null or not "y" or "yes")
            {
                Msg("Aborting. Have a nice day!");
                return;
            }
            else
            {
                Msg("Proceeding with Cumulo patches");
            }
        }


        DefaultAssemblyResolver resolver = new();
        readParams.AssemblyResolver = resolver;
        resolver.AddSearchDirectory(path); // Add the desired directory to the assembly resolution path
    

        // Read all of the targetModules from the headless directory and return their definitions
        IEnumerable<ModuleDefinition> getModules()
        {
            foreach (string moduleName in targetModules)
            {
                FileStream stream = File.Open(Path.Combine(path, moduleName), FileMode.Open, FileAccess.ReadWrite);
                yield return ModuleDefinition.ReadModule(stream, readParams);
            }
        }
        

        // Search the modules for any methods that need to be patched and execute the patches
        PatchHelpers.PatchAll([.. getModules()]);


        // Copy some extra libraries to the headless in order to satisfy it's new .NET 8 dependencies
        foreach (var (source, dest) in GetFiles(path))
        {
            Msg($"Copying {source} to {dest}");
            try
            {
                if (!Directory.Exists(dest))
                    Directory.CreateDirectory(dest);
                
                File.Copy(source, dest, true);
            }
            catch (Exception e)
            {
                Error($"Failed copying {source}, skipping. Exception: {e}");
            }
        }

        // Success! :D (Mono.Cecil has stripped away a part of me that I'll never get back, but in return, I now wield eldritch power... Was it worth it...?)
        Msg("Success!");

        if (noConfirm)
            return;
        
        Msg("Press any key to continue");
        Console.ReadKey();
    }


    /// <summary>
    /// Gets all of the extra libraries that need to be copied into the destination.
    /// </summary>
    /// <param name="dest"></param>
    /// <returns></returns>
    public static IEnumerable<(string source, string dest)> GetFiles(string dest)
    {
        yield return ("extralibs/0Harmony.dll", Path.Combine(dest, "Libraries"));
        yield return ("extralibs/System.Security.Permissions.dll", dest);
        yield return ("extralibs/Nimbus.dll", Path.Combine(dest, "rml_mods"));
        yield return ("extralibs/Resonite.runtimeconfig.json", dest);
    }


    /// <summary>
    /// Prints a message to console
    /// </summary>
    /// <param name="message"></param>
    public static void Msg(string message)
    {
        Console.WriteLine($"[INFO] {message}");
    }



    /// <summary>
    /// Prints a warning to console
    /// </summary>
    /// <param name="message"></param>
    public static void Warn(string message)
    {
        Console.WriteLine($"[WARN] {message}");
    }



    /// <summary>
    /// Prints an error to console
    /// </summary>
    /// <param name="message"></param>
    public static void Error(string message)
    {
        Console.WriteLine($"[ERROR] {message}");
    }



    /// <summary>
    /// Patches the "Initialize" method in "FrooxEngine.LibraryInitializer" to remove the Ssl3 protocol as it's unsupported.
    /// </summary>
    /// <param name="instructions"></param>
    /// <param name="module"></param>
    /// <param name="processor"></param>
    /// <returns></returns>
    [MethodPatch("FrooxEngine.LibraryInitializer", "Initialize")]
    public static IEnumerable<Instruction> Initialize_Patch(IEnumerable<Instruction> instructions, ModuleDefinition module, ILProcessor processor)
    {
        foreach (Instruction inst in instructions)
        {
            // Check if the opcode loads the bitmask for "SecurityProtocolType.Ssl3 | SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12
            if (inst.OpCode.Code == Code.Ldc_I4 && (int)inst.Operand == 0xFF0)
            {

                Msg("----------------");
                Msg("Removing SSL3 security protocol from ServicePointManager to prevent a crash due to deprecation");
                Msg("----------------\n");


                // Replace the old protocol bitmask with one that doesn't have Ssl3 in it as it's unsupported and will cause an exception in newer versions of .NET
                yield return processor.Create
                (
                    OpCodes.Ldc_I4,
                    (int)(SecurityProtocolType.Tls | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls12)
                );
            }
            // Otherwise return the instructions as normal
            else
            {
                yield return inst;
            }
        }
    }



    /// <summary>
    /// Patches the "Process" method in "FrooxEngine.Weaver.AssemblyPostProcessor" to 
    /// </summary>
    /// <param name="instructions"></param>
    /// <param name="module"></param>
    /// <param name="processor"></param>
    /// <returns></returns>
    [MethodPatch("FrooxEngine.Weaver.AssemblyPostProcessor", "Process", "String", "String&", "String")]
    public static IEnumerable<Instruction> Process_Patch(IEnumerable<Instruction> instructions, ModuleDefinition module, ILProcessor processor)
    {
        Msg("----------------");
        Msg("Snipping the assembly post processor to prevent crashes upon trying to process plugin libraries");
        Warn("This means any normal plugins that haven't been post-processed won't work! Load them in a normal headless first!!");
        Msg("----------------\n");


        // Make a new lambda definition (final result: string => string.ToLower())
        var lambda = new MethodDefinition("StringLambda", MethodAttributes.Public | MethodAttributes.Static, module.TypeSystem.String);
        
        // Add a string input parameter to the method
        var param = new ParameterDefinition("stringInput", ParameterAttributes.None, module.TypeSystem.String);
        
        // Add the parameter to the method definition
        lambda.Parameters.Add(param);

        // Emit the function body
        var lambdaProcessor = lambda.Body.GetILProcessor();
        lambdaProcessor.Emit(OpCodes.Ldarg_0);
        lambdaProcessor.Emit(OpCodes.Callvirt, module.ImportReference(typeof(string).GetMethod("ToLower", [])));
        lambdaProcessor.Emit(OpCodes.Ret);

        // Add the lambda into the module
        processor.Body.Method.DeclaringType.Methods.Add(lambda);

        // Find the first instruction so we can insert a jump point to it later.
        Instruction breakTarget = processor.Body.Instructions[0];


        /* Here, returnCheck generates the following code:

        if (!Environment.GetCommandLineArgs().Select(stringInput => stringInput.ToLower()).Contains("-allowpluginprocessing"))
        {
            Console.WriteLine("[NIMBUS] Skipped assembly processing to avoid Mono.Cecil crashing (TODO: Fix properly at some point)");
            versionNumber = "-1";
            return false;
        }
        */
        List<Instruction> returnCheck =
        [
            processor.Create(OpCodes.Call, module.ImportReference(PatchHelpers.GetMethodInfo(Environment.GetCommandLineArgs))),
            processor.Create(OpCodes.Ldnull),
            processor.Create(OpCodes.Ldftn, lambda),
            processor.Create(OpCodes.Newobj, module.ImportReference(typeof(Func<string, string>).GetConstructor([ typeof(object), typeof(IntPtr)]))),
            processor.Create(OpCodes.Call, module.ImportReference(PatchHelpers.GetMethodInfo((Func<IEnumerable<string>, Func<string, string>, IEnumerable<string>>)Enumerable.Select))),
            processor.Create(OpCodes.Ldstr, "-allowpluginprocessing"),
            processor.Create(OpCodes.Call, module.ImportReference(PatchHelpers.GetMethodInfo((Func<IEnumerable<string>, string, bool>)Enumerable.Contains))),
            processor.Create(OpCodes.Brtrue, breakTarget), // If the command line arg isn't present, jump to the rest of the function
            processor.Create(OpCodes.Ldstr, "[NIMBUS] Skipped assembly processing to avoid Mono.Cecil crashing (TODO: Fix properly at some point)"),
            processor.Create(OpCodes.Call, module.ImportReference(PatchHelpers.GetMethodInfo((Action<string>)Console.WriteLine))),
            processor.Create(OpCodes.Ldarg_1), // Load 'out' parameter "versionNumber"
            processor.Create(OpCodes.Ldstr, "-1"), // Load "-1" as a string
            processor.Create(OpCodes.Stind_Ref), // Set "versionNumber" to "-1"
            processor.Create(OpCodes.Ldc_I4_0), // Load zero so that "Ret" return false
            processor.Create(OpCodes.Ret)
        ];

        // Insert returnCheck at the beginning of the function
        foreach (Instruction inst in returnCheck)
            processor.InsertBefore(breakTarget, inst);
        
        
        return instructions;
    }
}