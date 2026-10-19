using System.Reflection;
using System.Text;

using DataMart.Application.Common.Behaviours;
using DataMart.Application.Common.Interfaces;
using DataMart.Application.ImportFile.Commands.ImportFile;
using DataMart.Application.ImportFile.Common;
using FluentValidation;
using Microsoft.Extensions.Hosting;

namespace Microsoft.Extensions.DependencyInjection;

public static class DependencyInjection
{
    static DependencyInjection()
    {
        // Register encoding provider for ExcelDataReader
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static void AddApplicationServices(this IHostApplicationBuilder builder)
    {
        builder.Services.AddAutoMapper(cfg => 
            cfg.AddMaps(Assembly.GetExecutingAssembly()));

        builder.Services.AddValidatorsFromAssembly(Assembly.GetExecutingAssembly());

        builder.Services.AddMediatR(cfg => {
            cfg.RegisterServicesFromAssembly(Assembly.GetExecutingAssembly());
            cfg.AddOpenRequestPreProcessor(typeof(LoggingBehaviour<>));
            cfg.AddOpenBehavior(typeof(UnhandledExceptionBehaviour<,>));
            cfg.AddOpenBehavior(typeof(AuthorizationBehaviour<,>));
            cfg.AddOpenBehavior(typeof(ValidationBehaviour<,>));
            cfg.AddOpenBehavior(typeof(PerformanceBehaviour<,>));
        });

        builder.Services.AddScoped<ImportFileExecutor>();
        builder.Services.AddScoped<ImportFileValidator>();

        // Journal-writer seam (journal framework spec §5.3): the import executor
        // writes tier-2 chunks and tier-1 counter/ID-range updates through it.
        builder.Services.AddScoped<IImportJournalWriter, ImportJournalWriter>();
    }
}
