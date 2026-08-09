using System;
using System.Collections.Generic;
using System.Text;
using DataMart.Application.EntityList.Queries.GetEntityList;

namespace DataMart.Application.Common.Interfaces;

public interface IEntitySchemaService
{
    EntitySchemaDto[] GetEntityList();
}
