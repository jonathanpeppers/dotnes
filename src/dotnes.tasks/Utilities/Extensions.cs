using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

namespace dotnes;

static class Extensions
{
	/// <summary>
	/// Get the bytes in an embedded resource as a Stream.
	/// WARNING: It is incorrect to read from this stream after the PEReader has been disposed.
	/// 
	/// See:
	///		https://github.com/dotnet/corefx/issues/23372
	///		https://gist.github.com/nguerrera/6864d2a907cb07d869be5a2afed8d764
	/// </summary>
	public static unsafe Stream GetEmbeddedResourceStream(this PEReader peReader, ManifestResource resource)
	{
		var header = peReader.PEHeaders.CorHeader;
		if (header == null)
			throw new BadImageFormatException("PEHeaders.CorHeader is null.");

		checked // arithmetic overflow here could cause AV
		{
			// Locate start and end of PE image in unmanaged memory.
			var block = peReader.GetEntireImage();
			Debug.Assert(block.Pointer != null && block.Length > 0);

			byte* peImageStart = block.Pointer;
			byte* peImageEnd = peImageStart + block.Length;

			// Locate offset to resources within PE image.
			int offsetToResources;
			if (!peReader.PEHeaders.TryGetDirectoryOffset(header.ResourcesDirectory, out offsetToResources))
			{
				throw new BadImageFormatException("Failed to get offset to resources in PE file.");
			}
			Debug.Assert(offsetToResources > 0);
			byte* resourceStart = peImageStart + offsetToResources + resource.Offset;

			// Get the length of the the resource from the first 4 bytes.
			if (resourceStart >= peImageEnd - sizeof(int))
			{
				throw new BadImageFormatException("resource offset out of bounds.");
			}

			int resourceLength = new BlobReader(resourceStart, sizeof(int)).ReadInt32();
			resourceStart += sizeof(int);
			if (resourceLength < 0 || resourceStart >= peImageEnd - resourceLength)
			{
				throw new BadImageFormatException("resource offset or length out of bounds.");
			}

			return new UnmanagedMemoryStream(resourceStart, resourceLength);
		}
	}

	public static string GetFullName(this TypeReference type, MetadataReader reader)
	{
		var ns = reader.GetString(type.Namespace);
		if (string.IsNullOrEmpty(ns))
			return reader.GetString(type.Name);
		return ns + "." + reader.GetString(type.Name);
	}

	public static string GetFullName(this TypeDefinition type, MetadataReader reader)
	{
		var ns = reader.GetString(type.Namespace);
		if (string.IsNullOrEmpty(ns))
			return reader.GetString(type.Name);
		return ns + "." + reader.GetString(type.Name);
	}
}
