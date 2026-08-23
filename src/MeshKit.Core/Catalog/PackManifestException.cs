namespace MeshKit.Core.Catalog;

public sealed class PackManifestException(string message, Exception? inner = null) : Exception(message, inner);
