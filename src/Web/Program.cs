using DataMart.Application.Common.Interfaces;
using DataMart.Infrastructure.Data;
using Microsoft.AspNetCore.Http.Features;
using Scalar.AspNetCore;
using System.Text;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.AddServiceDefaults();

builder.AddKeyVaultIfConfigured();
builder.AddApplicationServices();
builder.AddInfrastructureServices();
builder.AddWebServices();

builder.Services.Configure<FormOptions>(o =>
    o.MultipartBodyLengthLimit = 100_000_000);
builder.WebHost.ConfigureKestrel(o =>
    o.Limits.MaxRequestBodySize = 100_000_000);

builder.Services.AddSingleton<IEntitySchemaService, EntitySchemaService>();
builder.Services.AddSingleton<IBulkImporter, BulkImporter>();
builder.Services.AddAntiforgery(o =>
{
    // CSRF hardening: bind the antiforgery cookie to the conventional Angular
    // name and enforce SameSite=Strict (the browser will not send the cookie on
    // any cross-site request). The SPA does not read this cookie (it's HttpOnly
    // by default — JS can't, and shouldn't need to, read the secret); instead the
    // /api/antiforgery prime endpoint returns the matching request token in the
    // response body, and the SPA's XsrfInterceptor caches it and echoes it back
    // in the RequestVerificationToken header on state-changing requests.
    o.Cookie.Name = "XSRF-TOKEN";
    o.Cookie.SameSite = SameSiteMode.Strict;
    o.HeaderName = "RequestVerificationToken";
});

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    await app.InitialiseDatabaseAsync();
}
else
{
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseCors(static builder =>
    builder.AllowAnyMethod()
        .AllowAnyHeader()
        .AllowAnyOrigin());

app.UseAntiforgery();

app.UseFileServer();

app.MapOpenApi();
app.MapScalarApiReference();

app.UseExceptionHandler(options => { });


app.MapDefaultEndpoints();
app.MapEndpoints(typeof(Program).Assembly);

app.MapFallbackToFile("index.html");

app.Run();
