using System;
using System.Collections.Generic;
using System.Text;

namespace DataMart.Domain.Common;

/// <summary>
/// Marks the Class/Struct/Parameter to be excluded from Mapping.
/// </summary>

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Property, AllowMultiple = true,  Inherited = false)]
public class ExcludeAttribute : Attribute
{
}
