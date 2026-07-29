using System;
using System.Collections.Generic;
using System.Text;

namespace DataMart.Application.EntityList.Queries.GetEntityList;

public record class EntityPropertyDto(string Name, string DataType, bool IsNullable,
    int? MaxLength, int? Precision, int? Scale,
    bool IsPrimary, bool IsForeignKey, IReadOnlyCollection<string>? RefTables, bool IsUnique, bool IsExcluded);

public record class EntitySchemaDto(string Name, IReadOnlyCollection<string> PrimaryKey,
    IReadOnlyCollection<EntityPropertyDto> Properties);
