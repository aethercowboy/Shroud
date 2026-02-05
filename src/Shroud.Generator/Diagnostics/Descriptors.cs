using Microsoft.CodeAnalysis;
using System;
using System.Collections.Generic;
using System.Text;

namespace Shroud.Generator.Diagnostics
{
	internal static class Descriptors
	{
		public static readonly DiagnosticDescriptor Debug = new(
			id: "SHD000",
			title: "Generic Debug Message",
			messageFormat: "{0}",
			category: "Usage",
			defaultSeverity: DiagnosticSeverity.Error,
			isEnabledByDefault: true,
			description: "The decorator name specified in the Decorate attribute must be a valid C# identifier.");
	}
}