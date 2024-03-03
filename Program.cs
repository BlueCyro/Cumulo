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
    public const string EXTRA_LIBS = "./extralibs";
    public const string SPECIAL_LIBS = "./specials";

    // For some reason, the order of this matters. The process of reading and resolving assemblies appears to
    // access other assemblies in the folder in such a way that prevents opening them with even the most permissive
    // of file sharing flags. I spent almost 12 hours trying to figure this out. I give up.
    public static readonly string[] targetModules = ["FrooxEngine.dll", "FrooxEngine.Weaver.dll"];

#pragma warning restore CS1591

    /// <summary>
    /// Main entry point, uses System.CommandLine for CLI
    /// </summary>
    /// <param name="args">Command line arguments</param>
    /// <returns></returns>
    public static int Main(string[] args)
    {
        // Define root command
        RootCommand root = new("Patches the Resonite headless to be compatible with the .NET 8 Runtime");

        // Add a string path that'll be used as the headless path
        Argument<string> path = new(
            "path",
            "The path of the Resonite Headless directory");
        

        // Add a bool option to allow running without user input
        Option<bool> noConfirm = new(
            "--noconfirm",
            () => false,
            "Runs Cumulo without asking for user confirmation (useful in headless startup scripts)");


        // Add a bool option to not download Nimbus automatically
        Option<bool> noNimbus  = new(
            "--nonimbus",
            () => false,
            "Continues without automatically installing Nimbus");


        root.AddArgument(path);
        root.AddOption(noConfirm);


        // Define a handler for running the command line input
        root.SetHandler((path, noConfirm, noNimbus) => {            
            if (!Directory.Exists(path))
            {
                Console.WriteLine("Cannot find directory, check the path for errors and try again");
                return;
            }

            Execute(path, noConfirm, noNimbus);

        }, path, noConfirm, noNimbus);


        return root.Invoke(args);
    }



    /// <summary>
    /// Executes the patcher and re-weaves the targetted assemblies
    /// </summary>
    /// <param name="path">The path of the Resonite Headless folder</param>
    /// <param name="noConfirm">Whether to run without user input</param>
    /// <param name="noNimbus">Whether to skip downloading Nimbus</param>
    public static void Execute(string path, bool noConfirm, bool noNimbus)
    {   
        Msg("Starting Cumulo patcher!");


        // If the extra libraries somehow don't exist, blow up
        if (!Directory.Exists(EXTRA_LIBS) || !File.Exists(Path.Combine(SPECIAL_LIBS, "0Harmony.dll")))
        {
            Error("Cannot find extra libraries! Exiting!");
            return;
        }


        // Make super duper sure that the user wants to irreversibly modify their headless
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

            // Only pass if the user types "y" or "yes", anything else aborts
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
    
        
        string nimbusPath = Path.Combine(EXTRA_LIBS, "rml_mods", "Nimbus.dll");

        if (File.Exists(nimbusPath))
            File.Delete(nimbusPath);

    
        if (!noNimbus)
        {
            // Download the latest release of Nimbus
            Msg("Downloading the latest release of Nimbus...");
            using WebClient wc = new();
            try
            {
                wc.DownloadFile("https://github.com/RileyGuy/Nimbus/releases/latest/download/Nimbus.dll", nimbusPath);
                Msg("Complete!");
            }
            catch (WebException ex)
            {
                Error($"Error when downloading file: {ex.Message}");
            }
        }


        // Read all of the targetModules from the headless directory and apply patches
        IEnumerable<FileStream> files =
            targetModules.Select(m => 
                File.Open(Path.Combine(path, m), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite));
        

        // Make a new Nimbus resolver and add the Headless as another path to look for assemblies in
        NimbusAssemblyResolver resolver = new();
        resolver.AddSearchDirectory(path);
        ReaderParameters readParams = new() // Allow read/write
        {
            ReadWrite = true,
            AssemblyResolver = resolver
        };


        // Go over each module and patch it in sequence
        foreach (FileStream module in files)
        {
            try
            {
                using AssemblyDefinition asm = AssemblyDefinition.ReadAssembly(module, readParams);
                int patched = PatchHelpers.PatchAll(asm.MainModule);

                if (patched > 0)
                {
                    Msg($"Writing assembly to: {module.Name}");
                    asm.Write(module);
                }
            }
            catch (IOException ex) // Catch any potential violations. I caught a lot of these so I'm leaving this here to be sure :(
            {
                Msg($"Caught sharing violation! Message: {ex.Message}");
                return;
            }
            finally
            {
                module.Dispose();
            }
        }


        // Copy some extra libraries to the headless in order to satisfy it's new .NET 8 dependencies
        Msg("Merging extralibs into Headless folder");
        PatchHelpers.CopyDirectory("./extralibs", path, true, true);

        // Harmony dll for .NET 8
        string harmonySource = Path.Combine(SPECIAL_LIBS, "0Harmony.dll");

        // Specifically try to find 0Harmony to replace the old one, otherwise just drop into Libraries
        string harmonyFile =
            Directory.EnumerateFiles(path, "0Harmony.dll", SearchOption.AllDirectories).FirstOrDefault() ??
            Path.Combine(path, "Libraries", "0Harmony.dll");
        
        string harmonyPath = Path.GetDirectoryName(harmonyFile);


        Msg($"Copying 0Harmony to {harmonyFile}");
        if (!Directory.Exists(harmonyPath))
            Directory.CreateDirectory(harmonyPath);
        
        // Copy 0Harmony.dll to the destination
        File.Copy(harmonySource, harmonyFile, true);

        // Success! :D (Mono.Cecil has stripped away a part of me that I'll never get back, but in return, I now wield eldritch power... Was it worth it...?)
        Msg("Success!");

        if (noConfirm)
            return;
        
        Msg("Press any key to continue");
        Console.ReadKey();
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
    /// Prints a message when the project is in debug mode.
    /// </summary>
    /// <param name="message"></param>
    public static void Debug(string message)
    {
#if DEBUG
        Console.WriteLine($"[DEBUG] {message}");
#endif
        return;
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