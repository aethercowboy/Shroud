using Microsoft.CodeAnalysis;
using System;
using System.Reflection;

namespace Shroud.Generator.Utilities
{
	internal static class Resource
	{
		public static string GetEmbeddedResource(string resourceName)
		{
			var assembly = Assembly.GetCallingAssembly();

			using var stream = assembly.GetManifestResourceStream(resourceName)
				?? throw new ArgumentException($"Resource {resourceName} not found in assembly.");

			using var reader = new System.IO.StreamReader(stream);
			return reader.ReadToEnd();
		}
	}
}