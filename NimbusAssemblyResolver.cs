using Mono.Cecil;


namespace Cumulonimbus;

/// <summary>
/// Dummy resolver to bypass the cache on the default one to not encounter file sharing violations
/// </summary>
public class NimbusAssemblyResolver : BaseAssemblyResolver
{

}
