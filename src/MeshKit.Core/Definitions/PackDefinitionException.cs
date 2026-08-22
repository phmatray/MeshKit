namespace MeshKit.Core.Definitions;

public sealed class PackDefinitionException(string message, Exception? inner = null) : Exception(message, inner);
