using Mono.Cecil;

#pragma warning disable CS1591

namespace Cumulonimbus;

/// <summary>
/// Attribute that can be applied to a method in order to turn it into a Harmony-style transpiler-esque patch for <see cref="MethodDefinition"/>
/// </summary>
/// <param name="typeName">The target type for the patch</param>
/// <param name="methodName">The method to apply the patch to</param>
/// <param name="methodSignature">Optional method signature types</param>
[AttributeUsage(AttributeTargets.Method)]
public class MethodPatchAttribute(string typeName, string methodName, params string[] methodSignature) : Attribute
{
    public string TypeName => typeName;
    public string MethodName => methodName;
    public string[] Signature => methodSignature;
}


#pragma warning restore CS1591